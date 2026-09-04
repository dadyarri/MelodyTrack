import { useQuery } from "@tanstack/react-query";
import { Alert, Button, Checkbox, DatePicker, Form, Modal, Space, Typography } from "antd";
import type { Dayjs } from "dayjs";
import { useEffect } from "react";

import { userQueryKeys, usersApi } from "@/entities/user";
import { DeleteOutlined } from "@/shared/ui/icons";

export type VacationRangeFormValues = {
  period: [Dayjs, Dayjs];
  cancelConflictingAppointments?: boolean;
};

export function VacationRangeModal({
  initialPeriod,
  open,
  pending,
  requestApproval,
  editing = false,
  removePending = false,
  subjectId,
  subjectName,
  onCancel,
  onRemove,
  onSubmit,
}: {
  initialPeriod: [Dayjs, Dayjs] | null;
  open: boolean;
  pending: boolean;
  requestApproval: boolean;
  editing?: boolean;
  removePending?: boolean;
  subjectId?: string;
  subjectName?: string;
  onCancel: () => void;
  onRemove?: () => void;
  onSubmit: (values: VacationRangeFormValues) => void;
}) {
  const [form] = Form.useForm<VacationRangeFormValues>();
  const selectedPeriod = Form.useWatch("period", form) as VacationRangeFormValues["period"] | undefined;
  const selectedStart = selectedPeriod?.[0].isValid() ? selectedPeriod[0].toISOString() : undefined;
  const selectedEnd = selectedPeriod?.[1].isValid() ? selectedPeriod[1].toISOString() : undefined;
  const canCheckConflicts = open && !requestApproval && Boolean(subjectId && selectedStart && selectedEnd);
  const conflictCountQuery = useQuery({
    queryKey: userQueryKeys.vacationAppointmentConflictCount(subjectId, selectedStart, selectedEnd),
    queryFn: ({ signal }) =>
      subjectId && selectedStart && selectedEnd
        ? usersApi.getVacationAppointmentConflictCount(subjectId, selectedStart, selectedEnd, signal)
        : Promise.resolve(0),
    enabled: canCheckConflicts,
    refetchOnMount: "always",
    retry: false,
    staleTime: 0,
  });
  const conflictCount = conflictCountQuery.data ?? 0;

  useEffect(() => {
    if (open && initialPeriod) {
      form.setFieldsValue({ period: initialPeriod, cancelConflictingAppointments: false });
    }
  }, [form, initialPeriod, open]);

  return (
    <Modal
      open={open}
      title={editing ? "Изменить отпуск" : requestApproval ? "Запросить отпуск" : "Добавить отпуск"}
      okText={editing ? "Сохранить" : requestApproval ? "Отправить заявку" : "Добавить"}
      confirmLoading={pending || conflictCountQuery.isFetching}
      okButtonProps={{ disabled: conflictCountQuery.isFetching }}
      onCancel={onCancel}
      onOk={() => {
        form.submit();
      }}
      footer={
        onRemove
          ? (_, { CancelBtn, OkBtn }) => (
              <Space className="wide" style={{ justifyContent: "space-between" }}>
                <Button danger icon={<DeleteOutlined />} loading={removePending} disabled={pending} onClick={onRemove}>
                  Удалить
                </Button>
                <Space>
                  <CancelBtn />
                  <OkBtn />
                </Space>
              </Space>
            )
          : undefined
      }
    >
      <Typography.Paragraph type="secondary">
        {editing
          ? `Изменения${subjectName ? ` для ${subjectName}` : ""} сразу повлияют на доступность.`
          : requestApproval
            ? "Период начнёт влиять на расписание после решения суперпользователя."
            : `Период будет добавлен${subjectName ? ` для ${subjectName}` : ""} и сразу начнёт влиять на доступность.`}
        {" Можно выбрать период на несколько недель."}
      </Typography.Paragraph>
      <Form<VacationRangeFormValues>
        form={form}
        layout="vertical"
        requiredMark={false}
        onFinish={onSubmit}
        onValuesChange={(changedValues: Partial<VacationRangeFormValues>) => {
          if ("period" in changedValues) {
            form.setFieldValue("cancelConflictingAppointments", false);
          }
        }}
      >
        <Form.Item
          name="period"
          label="Начало и окончание"
          rules={[
            { required: true, message: "Укажите период отпуска" },
            {
              validator: (_, value: [Dayjs, Dayjs] | undefined) =>
                value?.[1]?.isAfter(value[0]) ? Promise.resolve() : Promise.reject(new Error("Окончание должно быть позже начала")),
            },
          ]}
        >
          <DatePicker.RangePicker className="wide" format="DD.MM.YYYY HH:mm" showTime={{ format: "HH:mm", minuteStep: 15 }} />
        </Form.Item>
        {!requestApproval && !conflictCountQuery.isFetching && conflictCount > 0 ? (
          <Alert
            type="warning"
            showIcon
            title={`Пересекающихся запланированных занятий: ${String(conflictCount)}.`}
            description={
              <Form.Item name="cancelConflictingAppointments" valuePropName="checked" noStyle>
                <Checkbox>Отменить пересекающиеся запланированные занятия и сохранить отпуск</Checkbox>
              </Form.Item>
            }
          />
        ) : null}
      </Form>
    </Modal>
  );
}
