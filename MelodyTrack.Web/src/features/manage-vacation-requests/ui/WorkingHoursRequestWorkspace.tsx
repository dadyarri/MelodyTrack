import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, App as AntdApp, Button, Card, Empty, Form, Input, List, Modal, Segmented, Space, Tag, Typography } from "antd";
import { useState } from "react";

import {
  weekdayLabels,
  weekdayOrder,
  type WorkingHoursRequest,
  type WorkingHoursRequestDay,
  workingHoursRequestQueryKeys,
  workingHoursRequestsApi,
  type WorkingHoursRequestStatus,
} from "@/entities/user";

import styles from "./VacationRequestWorkspace.module.css";

type DecisionFormValues = { message?: string };
type DecisionState = { action: "approve" | "decline"; request: WorkingHoursRequest };

export function WorkingHoursRequestWorkspace({ mode }: { mode: "staff" | "review" }) {
  const { message } = AntdApp.useApp();
  const queryClient = useQueryClient();
  const [decisionForm] = Form.useForm<DecisionFormValues>();
  const [reviewView, setReviewView] = useState<"pending" | "history">("pending");
  const [decision, setDecision] = useState<DecisionState | null>(null);
  const review = mode === "review";
  const query = useQuery({
    queryKey: review ? workingHoursRequestQueryKeys.review(reviewView) : workingHoursRequestQueryKeys.mine,
    queryFn: ({ signal }) => (review ? workingHoursRequestsApi.listReview(reviewView, signal) : workingHoursRequestsApi.listMine(signal)),
  });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: workingHoursRequestQueryKeys.all });

  const cancelMutation = useMutation({
    mutationFn: (request: WorkingHoursRequest) => workingHoursRequestsApi.cancel(request.id, request.version),
    onSuccess: async () => {
      await invalidate();
      void message.success("Заявка отозвана");
    },
  });

  const decisionMutation = useMutation({
    mutationFn: ({ state, values }: { state: DecisionState; values: DecisionFormValues }) => {
      const input = { expectedVersion: state.request.version, message: values.message?.trim() || undefined };
      return state.action === "approve"
        ? workingHoursRequestsApi.approve(state.request.id, input)
        : workingHoursRequestsApi.decline(state.request.id, input);
    },
    onSuccess: async (_, variables) => {
      setDecision(null);
      decisionForm.resetFields();
      await invalidate();
      void message.success(variables.state.action === "approve" ? "Рабочие дни согласованы" : "Заявка отклонена");
    },
  });

  return (
    <Space orientation="vertical" size={20} className="wide">
      {review ? (
        <Segmented
          value={reviewView}
          options={[
            { label: "Ожидают решения", value: "pending" },
            { label: "История", value: "history" },
          ]}
          onChange={(value) => {
            setReviewView(value as "pending" | "history");
          }}
        />
      ) : (
        <Alert
          type="info"
          showIcon
          title="Изменить рабочие дни можно в профиле. Новый график начнёт действовать только после одобрения суперпользователем."
        />
      )}

      <Card title={review ? "Заявки на изменение рабочих дней" : "Мои заявки на рабочие дни"} loading={query.isLoading}>
        {query.isError ? (
          <Alert
            type="error"
            showIcon
            title="Не удалось загрузить заявки на рабочие дни"
            action={<Button onClick={() => void query.refetch()}>Повторить</Button>}
          />
        ) : (
          <List
            dataSource={query.data ?? []}
            locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Заявок пока нет" /> }}
            renderItem={(request) => (
              <List.Item className={styles.requestItem}>
                <div className={styles.requestBody}>
                  <div className={styles.requestHeading}>
                    <div>
                      <Typography.Title level={5} className={styles.requestTitle}>
                        {review ? request.subjectName : `Заявка от ${formatDateTime(request.createdAtUtc)}`}
                      </Typography.Title>
                      {review ? <Typography.Text type="secondary">{request.subjectClassification}</Typography.Text> : null}
                    </div>
                    <StatusTag status={request.status} />
                  </div>
                  {request.requestMessage ? <Typography.Paragraph>{request.requestMessage}</Typography.Paragraph> : null}
                  {request.decisionMessage ? (
                    <Typography.Paragraph type="secondary">Комментарий к решению: {request.decisionMessage}</Typography.Paragraph>
                  ) : null}
                  <div className={styles.scheduleComparison}>
                    {review ? <ScheduleSummary title="Текущий график" days={request.currentWorkingHours} /> : null}
                    <ScheduleSummary title="Запрошенный график" days={request.requestedWorkingHours} />
                  </div>
                  {review ? <Typography.Text type="secondary">Создана {formatDateTime(request.createdAtUtc)}</Typography.Text> : null}
                  <div className={styles.actions}>
                    {!review && request.status === "pending" ? (
                      <Button
                        danger
                        loading={cancelMutation.isPending}
                        onClick={() => {
                          cancelMutation.mutate(request);
                        }}
                      >
                        Отозвать
                      </Button>
                    ) : null}
                    {review && request.status === "pending" ? (
                      <>
                        <Button
                          type="primary"
                          onClick={() => {
                            decisionForm.resetFields();
                            setDecision({ action: "approve", request });
                          }}
                        >
                          Одобрить
                        </Button>
                        <Button
                          danger
                          onClick={() => {
                            decisionForm.resetFields();
                            setDecision({ action: "decline", request });
                          }}
                        >
                          Отклонить
                        </Button>
                      </>
                    ) : null}
                  </div>
                </div>
              </List.Item>
            )}
          />
        )}
      </Card>

      <Modal
        open={decision !== null}
        title={decision?.action === "approve" ? "Одобрить новые рабочие дни?" : "Отклонить заявку?"}
        okText={decision?.action === "approve" ? "Одобрить" : "Отклонить"}
        okButtonProps={{ danger: decision?.action === "decline", loading: decisionMutation.isPending }}
        onCancel={() => {
          setDecision(null);
        }}
        onOk={() => {
          decisionForm.submit();
        }}
      >
        <Form<DecisionFormValues>
          form={decisionForm}
          layout="vertical"
          onFinish={(values) => {
            if (decision) decisionMutation.mutate({ state: decision, values });
          }}
        >
          <Form.Item name="message" label="Комментарий" rules={[{ max: 500, message: "Не больше 500 символов" }]}>
            <Input.TextArea rows={3} maxLength={500} showCount />
          </Form.Item>
        </Form>
      </Modal>
    </Space>
  );
}

function ScheduleSummary({ title, days }: { title: string; days: WorkingHoursRequestDay[] }) {
  const byDay = new Map(days.map((day) => [day.dayOfWeek, day]));
  return (
    <div className={styles.scheduleCard}>
      <Typography.Text strong>{title}</Typography.Text>
      {weekdayOrder.map((dayKey) => {
        const day = byDay.get(dayKey);
        return (
          <div className={styles.scheduleRow} key={dayKey}>
            <Typography.Text>{weekdayLabels[dayKey]}</Typography.Text>
            <Typography.Text type={day?.isWorkingDay ? undefined : "secondary"}>
              {day?.isWorkingDay ? `${day.startTime ?? "—"}–${day.endTime ?? "—"}` : "Выходной"}
            </Typography.Text>
          </div>
        );
      })}
    </div>
  );
}

function StatusTag({ status }: { status: WorkingHoursRequestStatus }) {
  const presentation: Record<WorkingHoursRequestStatus, { color: string; label: string }> = {
    pending: { color: "processing", label: "Ожидает решения" },
    approved: { color: "success", label: "Одобрена" },
    declined: { color: "error", label: "Отклонена" },
    cancelled: { color: "default", label: "Отозвана" },
  };
  return <Tag color={presentation[status].color}>{presentation[status].label}</Tag>;
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat("ru-RU", { dateStyle: "medium", timeStyle: "short" }).format(new Date(value));
}
