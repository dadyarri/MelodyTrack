import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, App as AntdApp, Button, Card, Checkbox, Empty, Form, Input, List, Modal, Segmented, Space, Tag, Typography } from "antd";
import type { Dayjs } from "dayjs";
import { useState } from "react";

import { vacationRequestQueryKeys, vacationRequestsApi } from "../api/vacationRequestApi";
import { invalidateScheduling } from "../lib/invalidateScheduling";
import type { VacationRequest, VacationRequestStatus } from "../model/types";
import { PortalVacationRequests } from "./PortalVacationRequests";
import { VacationManagement } from "./VacationManagement";
import styles from "./VacationRequestWorkspace.module.css";

type RequestFormValues = {
  period: [Dayjs, Dayjs];
  message?: string;
};

type DecisionFormValues = {
  message?: string;
  cancelConflictingAppointments?: boolean;
};

type DecisionState = {
  action: "approve" | "decline";
  request: VacationRequest;
};

export function VacationRequestWorkspace({ mode }: { mode: "staff" | "portal" | "review" }) {
  const { message } = AntdApp.useApp();
  const queryClient = useQueryClient();
  const [decisionForm] = Form.useForm<DecisionFormValues>();
  const [reviewView, setReviewView] = useState<"pending" | "history">("pending");
  const [decision, setDecision] = useState<DecisionState | null>(null);
  const portal = mode === "portal";
  const review = mode === "review";
  const query = useQuery({
    queryKey: review ? vacationRequestQueryKeys.review(reviewView) : vacationRequestQueryKeys.mine(portal),
    queryFn: ({ signal }) => (review ? vacationRequestsApi.listReview(reviewView, signal) : vacationRequestsApi.listMine(portal, signal)),
  });

  const invalidate = () => invalidateScheduling(queryClient);

  const createMutation = useMutation({
    mutationFn: (values: RequestFormValues) =>
      vacationRequestsApi.create(
        {
          startDate: values.period[0].toISOString(),
          endDate: values.period[1].toISOString(),
          message: values.message?.trim() || undefined,
        },
        portal,
      ),
    onSuccess: async () => {
      await invalidate();
      if (!portal) void message.success("Заявка отправлена суперпользователю");
    },
  });

  const cancelMutation = useMutation({
    mutationFn: (request: VacationRequest) => vacationRequestsApi.cancel(request.id, request.version),
    onSuccess: async () => {
      await invalidate();
      if (!portal) void message.success("Заявка отозвана");
    },
  });

  const decisionMutation = useMutation({
    mutationFn: ({ state, values }: { state: DecisionState; values: DecisionFormValues }) => {
      const input = {
        expectedVersion: state.request.version,
        message: values.message?.trim() || undefined,
        cancelConflictingAppointments: values.cancelConflictingAppointments,
      };
      return state.action === "approve"
        ? vacationRequestsApi.approve(state.request.id, input)
        : vacationRequestsApi.decline(state.request.id, input);
    },
    onSuccess: async (_, variables) => {
      setDecision(null);
      decisionForm.resetFields();
      await invalidate();
      void message.success(variables.state.action === "approve" ? "Отпуск согласован" : "Заявка отклонена");
    },
  });

  const requests = query.data ?? [];

  if (portal)
    return (
      <Space orientation="vertical" className="wide">
        <VacationManagement allowCreate={false} />
        <PortalVacationRequests
          requests={requests}
          loading={query.isLoading}
          error={query.error}
          cancelled={cancelMutation.isSuccess}
          mutationError={createMutation.error ?? cancelMutation.error}
          creating={createMutation.isPending}
          cancelling={cancelMutation.isPending}
          onRetry={() => {
            void query.refetch();
          }}
          onCreate={createMutation.mutateAsync}
          onCancel={(request) => {
            cancelMutation.mutate(request);
          }}
        />
      </Space>
    );

  return (
    <Space orientation="vertical" size={20} className="wide">
      {!review ? (
        <VacationManagement />
      ) : (
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
      )}

      <Card title={review ? "Очередь заявок" : "Мои заявки"} loading={query.isLoading}>
        {query.isError ? (
          <Alert
            type="error"
            showIcon
            title="Не удалось загрузить заявки"
            action={<Button onClick={() => void query.refetch()}>Повторить</Button>}
          />
        ) : (
          <List
            dataSource={requests}
            locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Заявок пока нет" /> }}
            renderItem={(request) => (
              <List.Item className={styles.requestItem}>
                <div className={styles.requestBody}>
                  <div className={styles.requestHeading}>
                    <div>
                      <Typography.Title level={5} className={styles.requestTitle}>
                        {review ? request.subjectName : formatPeriod(request.startDate, request.endDate)}
                      </Typography.Title>
                      {review ? (
                        <Typography.Text type="secondary">
                          {request.subjectClassification} · заявитель: {request.requesterName}
                        </Typography.Text>
                      ) : null}
                    </div>
                    <StatusTag status={request.status} />
                  </div>
                  {review ? <Typography.Text strong>{formatPeriod(request.startDate, request.endDate)}</Typography.Text> : null}
                  {request.requestMessage ? <Typography.Paragraph>{request.requestMessage}</Typography.Paragraph> : null}
                  {request.decisionMessage ? (
                    <Typography.Paragraph type="secondary">Комментарий к решению: {request.decisionMessage}</Typography.Paragraph>
                  ) : null}
                  {review && request.existingVacations.length > 0 ? (
                    <Typography.Text type="secondary">
                      Текущие отпуска: {request.existingVacations.map((item) => formatPeriod(item.startDate, item.endDate)).join(", ")}
                    </Typography.Text>
                  ) : null}
                  <Tag>
                    {request.action === "update" ? "Изменение отпуска" : request.action === "delete" ? "Удаление отпуска" : "Новый отпуск"}
                  </Tag>
                  <Space wrap size={[8, 6]}>
                    <Typography.Text type="secondary">Создана {formatDateTime(request.createdAtUtc)}</Typography.Text>
                    {review && request.existingVacations.length > 0 ? <Tag>Отпусков уже: {request.existingVacations.length}</Tag> : null}
                    {review && request.conflictingAppointmentCount > 0 ? (
                      <Tag color="warning">Конфликтующих занятий: {request.conflictingAppointmentCount}</Tag>
                    ) : null}
                  </Space>
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
        title={decision?.action === "approve" ? "Одобрить отпуск?" : "Отклонить заявку?"}
        okText={decision?.action === "approve" ? "Одобрить" : "Отклонить"}
        okButtonProps={{ danger: decision?.action === "decline", loading: decisionMutation.isPending }}
        onCancel={() => {
          setDecision(null);
        }}
        onOk={() => {
          decisionForm.submit();
        }}
      >
        {decision?.action === "approve" && decision.request.conflictingAppointmentCount > 0 ? (
          <Alert
            type="warning"
            showIcon
            className={styles.notice}
            title={`Есть конфликты расписания: ${String(decision.request.conflictingAppointmentCount)}.`}
            description="Можно явно отменить только пересекающиеся запланированные занятия. Завершённые занятия и правило повторения не изменятся."
          />
        ) : null}
        <Form<DecisionFormValues>
          form={decisionForm}
          layout="vertical"
          onFinish={(values) => {
            if (decision) {
              decisionMutation.mutate({ state: decision, values });
            }
          }}
        >
          <Form.Item name="message" label="Комментарий" rules={[{ max: 500, message: "Не больше 500 символов" }]}>
            <Input.TextArea rows={3} maxLength={500} showCount />
          </Form.Item>
          {decision?.action === "approve" && decision.request.action !== "delete" ? (
            <Form.Item name="cancelConflictingAppointments" valuePropName="checked">
              <Checkbox>Отменить пересекающиеся запланированные занятия, если они есть, и одобрить отпуск</Checkbox>
            </Form.Item>
          ) : null}
        </Form>
      </Modal>
    </Space>
  );
}

function StatusTag({ status }: { status: VacationRequestStatus }) {
  const statusPresentation: Record<VacationRequestStatus, { color: string; label: string }> = {
    pending: { color: "processing", label: "Ожидает решения" },
    approved: { color: "success", label: "Одобрена" },
    declined: { color: "error", label: "Отклонена" },
    cancelled: { color: "default", label: "Отозвана" },
  };
  const presentation = statusPresentation[status];
  return <Tag color={presentation.color}>{presentation.label}</Tag>;
}

function formatPeriod(startDate: string, endDate: string) {
  return `${formatDateTime(startDate)} — ${formatDateTime(endDate)}`;
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat("ru-RU", { dateStyle: "medium", timeStyle: "short" }).format(new Date(value));
}
