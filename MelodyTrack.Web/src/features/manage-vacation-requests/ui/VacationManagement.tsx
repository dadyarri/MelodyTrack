import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Card, Checkbox, DatePicker, Empty, Form, Input, List, Modal, Space, Tag, Typography } from "antd";
import dayjs, { type Dayjs } from "dayjs";
import { useState } from "react";

import { useAuth } from "@/entities/session";
import { userQueryKeys, usersApi } from "@/entities/user";
import { type Vacation, vacationQueryKeys, vacationsApi, type VacationSubject } from "@/entities/vacation";
import { getApiErrorMessage } from "@/shared/api";
import { browserTimezone, teacherTimeToUtc, toTeacherTime } from "@/shared/lib";

import { invalidateScheduling } from "../lib/invalidateScheduling";

type VacationForm = { period: [Dayjs, Dayjs]; message?: string; cancelConflictingAppointments?: boolean };
type Operation = { action: "edit" | "delete" | "end"; vacation: Vacation } | { action: "create" };
const states: Record<string, string> = {
  scheduled: "Запланирован",
  active: "Сейчас в отпуске",
  ended: "Завершён досрочно",
  completed: "Завершён",
  deleted: "Удалён",
};

export function VacationManagement({ subject, allowCreate = true }: { subject?: VacationSubject; allowCreate?: boolean }) {
  const queryClient = useQueryClient();
  const [form] = Form.useForm<VacationForm>();
  const [operation, setOperation] = useState<Operation | null>(null);
  const [restoration, setRestoration] = useState<Vacation | null>(null);
  const [selected, setSelected] = useState<string[]>([]);
  const [notice, setNotice] = useState<string | null>(null);
  const auth = useAuth();
  const staffId = subject?.subjectType === "staff" ? subject.subjectId : !subject && !auth.user?.linkedClientId ? auth.user?.id : undefined;
  const availability = useQuery({
    queryKey: userQueryKeys.availability(staffId),
    queryFn: () => {
      if (!staffId) throw new Error("Преподаватель не выбран.");
      return usersApi.getAvailability(staffId);
    },
    enabled: Boolean(staffId),
  });
  const zone = availability.data?.timezone ?? browserTimezone;
  const query = useQuery({ queryKey: vacationQueryKeys.list(subject), queryFn: ({ signal }) => vacationsApi.list(subject, signal) });
  const restorationId = restoration?.id;
  const candidates = useQuery({
    queryKey: vacationQueryKeys.candidates(restorationId, restoration?.version),
    queryFn: ({ signal }) => {
      if (!restorationId) throw new Error("Отпуск не выбран.");
      return vacationsApi.candidates(restorationId, signal);
    },
    enabled: Boolean(restoration),
    staleTime: 0,
  });
  const open = (next: Operation) => {
    setNotice(null);
    mutation.reset();
    form.resetFields();
    if (next.action === "edit")
      form.setFieldsValue({ period: [toTeacherTime(next.vacation.startDate, zone), toTeacherTime(next.vacation.endDate, zone)] });
    if (next.action === "create")
      form.setFieldsValue({ period: [toTeacherTime(dayjs(), zone), toTeacherTime(dayjs().add(1, "day"), zone)] });
    setOperation(next);
  };
  const mutation = useMutation({
    mutationFn: async ({ operation: current, values }: { operation: Operation; values: VacationForm }) => {
      if (current.action === "delete") return vacationsApi.remove(current.vacation.id, current.vacation.version, values.message);
      if (current.action === "end") return vacationsApi.end(current.vacation.id, current.vacation.version);
      const input = {
        startDate: teacherTimeToUtc(values.period[0], zone),
        endDate: teacherTimeToUtc(values.period[1], zone),
        message: values.message?.trim() || undefined,
        cancelConflictingAppointments: values.cancelConflictingAppointments,
      };
      return current.action === "create"
        ? vacationsApi.create({ ...input, ...subject })
        : vacationsApi.update(current.vacation.id, { ...input, expectedVersion: current.vacation.version });
    },
    onSuccess: async (result, { operation: current }) => {
      setOperation(null);
      form.resetFields();
      setNotice(
        result.requiresApproval
          ? "Заявка отправлена. До согласования отпуск продолжает действовать на прежних условиях."
          : "Отпуск обновлён. Занятия восстанавливаются только по вашему выбору.",
      );
      if (current.action !== "create" && !result.requiresApproval && result.vacation) {
        setSelected([]);
        setRestoration(result.vacation as Vacation);
      }
      await invalidateScheduling(queryClient);
    },
    onError: () => {
      void invalidateScheduling(queryClient);
    },
  });
  const restore = useMutation({
    mutationFn: () => {
      if (!restoration) throw new Error("Отпуск не выбран.");
      return vacationsApi.restore(restoration.id, { expectedVersion: restoration.version, interruptionIds: selected });
    },
    onSuccess: async () => {
      setRestoration(null);
      setSelected([]);
      setNotice("Выбранные занятия восстановлены.");
      await invalidateScheduling(queryClient);
    },
    onError: () => {
      setSelected([]);
      void candidates.refetch();
      void invalidateScheduling(queryClient);
    },
  });
  return (
    <Card
      title="Отпуска"
      extra={
        allowCreate ? (
          <Button
            onClick={() => {
              open({ action: "create" });
            }}
            disabled={Boolean(staffId) && !availability.data}
          >
            Добавить отпуск
          </Button>
        ) : undefined
      }
    >
      <Space orientation="vertical" className="wide" size="middle">
        {notice ? <Alert type="success" title={notice} showIcon /> : null}
        {availability.error ? (
          <Alert
            type="error"
            title={getApiErrorMessage(availability.error)}
            action={<Button onClick={() => void availability.refetch()}>Повторить</Button>}
          />
        ) : null}
        {query.error ? (
          <Alert
            type="error"
            title={getApiErrorMessage(query.error)}
            action={<Button onClick={() => void query.refetch()}>Повторить</Button>}
          />
        ) : null}
        <List
          loading={query.isLoading}
          dataSource={query.data ?? []}
          locale={{ emptyText: <Empty description="Отпусков пока нет" /> }}
          renderItem={(vacation) => (
            <List.Item>
              <Space orientation="vertical" className="wide">
                <Typography.Text strong>
                  {toTeacherTime(vacation.startDate, zone).format("DD.MM.YYYY HH:mm")} —{" "}
                  {toTeacherTime(vacation.endDate, zone).format("DD.MM.YYYY HH:mm")} ({zone})
                </Typography.Text>
                <Tag>{states[vacation.state] ?? vacation.state}</Tag>
                <Space wrap>
                  {vacation.canManage && (vacation.state === "scheduled" || vacation.state === "active") ? (
                    <>
                      <Button
                        disabled={Boolean(staffId) && !availability.data}
                        onClick={() => {
                          open({ action: "edit", vacation });
                        }}
                      >
                        Изменить
                      </Button>
                      <Button
                        danger
                        onClick={() => {
                          open({ action: "delete", vacation });
                        }}
                      >
                        Удалить
                      </Button>
                    </>
                  ) : null}
                  {vacation.canEnd ? (
                    <Button
                      onClick={() => {
                        open({ action: "end", vacation });
                      }}
                    >
                      Завершить сейчас
                    </Button>
                  ) : null}
                  {vacation.canManage ? (
                    <Button
                      onClick={() => {
                        setSelected([]);
                        restore.reset();
                        setRestoration(vacation);
                      }}
                    >
                      Восстановить занятия
                    </Button>
                  ) : null}
                </Space>
              </Space>
            </List.Item>
          )}
        />
      </Space>
      <Modal
        open={operation !== null}
        title={
          operation?.action === "create"
            ? "Добавить отпуск"
            : operation?.action === "edit"
              ? "Изменить отпуск"
              : operation?.action === "end"
                ? "Завершить отпуск сейчас?"
                : "Удалить отпуск?"
        }
        okText={
          operation?.action === "end"
            ? "Завершить сейчас"
            : operation && "vacation" in operation && operation.vacation.requiresApproval
              ? "Отправить заявку"
              : "Сохранить"
        }
        confirmLoading={mutation.isPending}
        onCancel={() => {
          setOperation(null);
        }}
        onOk={() => {
          form.submit();
        }}
      >
        <Form<VacationForm>
          form={form}
          layout="vertical"
          onFinish={(values) => {
            if (operation) mutation.mutate({ operation, values });
          }}
        >
          <Typography.Paragraph>
            Досрочное завершение действует сразу. Изменение и удаление собственного отпуска согласует суперпользователь.
          </Typography.Paragraph>
          {operation?.action === "edit" || operation?.action === "create" ? (
            <>
              <Form.Item
                name="period"
                label={`Начало и окончание (${zone})`}
                rules={[
                  { required: true, message: "Укажите период" },
                  {
                    validator: (_, value: [Dayjs, Dayjs] | undefined) =>
                      value?.[1]?.isAfter(value[0]) ? Promise.resolve() : Promise.reject(new Error("Окончание должно быть позже начала")),
                  },
                ]}
              >
                <DatePicker.RangePicker className="wide" format="DD.MM.YYYY HH:mm" showTime={{ format: "HH:mm", minuteStep: 15 }} />
              </Form.Item>
              {auth.user?.isSuperuser ? (
                <Form.Item name="cancelConflictingAppointments" valuePropName="checked">
                  <Checkbox>Отменить пересекающиеся запланированные занятия</Checkbox>
                </Form.Item>
              ) : null}
            </>
          ) : null}
          {operation?.action !== "end" ? (
            <Form.Item name="message" label="Комментарий">
              <Input.TextArea maxLength={500} rows={3} />
            </Form.Item>
          ) : null}
          {mutation.error ? <Alert type="error" title={getApiErrorMessage(mutation.error)} /> : null}
        </Form>
      </Modal>
      <Modal
        open={Boolean(restoration)}
        title="Восстановление будущих занятий"
        okText="Восстановить выбранные"
        okButtonProps={{ disabled: selected.length === 0 }}
        confirmLoading={restore.isPending}
        onCancel={() => {
          setRestoration(null);
        }}
        onOk={() => {
          restore.mutate();
        }}
      >
        <Typography.Paragraph>
          Проверяется отпуск обеих сторон, рабочее время и занятость. Невыбранные занятия остаются отменёнными.
        </Typography.Paragraph>
        {candidates.error ? (
          <Alert
            type="error"
            title={getApiErrorMessage(candidates.error)}
            action={<Button onClick={() => void candidates.refetch()}>Повторить</Button>}
          />
        ) : null}
        {restore.error ? <Alert type="error" title={getApiErrorMessage(restore.error)} /> : null}
        <List
          loading={candidates.isFetching}
          dataSource={candidates.data ?? []}
          locale={{ emptyText: "Подходящих будущих занятий нет" }}
          renderItem={(candidate) => (
            <List.Item>
              <Space orientation="vertical">
                <Checkbox
                  disabled={!candidate.available}
                  checked={selected.includes(candidate.id)}
                  onChange={(event) => {
                    setSelected((ids) => (event.target.checked ? [...ids, candidate.id] : ids.filter((id) => id !== candidate.id)));
                  }}
                >
                  {toTeacherTime(candidate.startDate, zone).format("DD.MM.YYYY HH:mm")} · {candidate.serviceName}
                </Checkbox>
                {!candidate.available ? <Typography.Text type="secondary">{candidate.blockers.join(" ")}</Typography.Text> : null}
              </Space>
            </List.Item>
          )}
        />
      </Modal>
    </Card>
  );
}
