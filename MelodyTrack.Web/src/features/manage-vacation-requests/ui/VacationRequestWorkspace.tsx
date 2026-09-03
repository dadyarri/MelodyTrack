import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, App as AntdApp, Button, Card, DatePicker, Empty, Form, Input, List, Modal, Segmented, Space, Tag, Typography } from "antd";
import type { Dayjs } from "dayjs";
import { useState } from "react";

import { getApiErrorMessages } from "@/shared/api";

import { vacationRequestQueryKeys, vacationRequestsApi } from "../api/vacationRequestApi";
import type { VacationRequest, VacationRequestStatus } from "../model/types";
import styles from "./VacationRequestWorkspace.module.css";

type RequestFormValues = {
  period: [Dayjs, Dayjs];
  message?: string;
};

type DecisionFormValues = {
  message?: string;
};

type DecisionState = {
  action: "approve" | "decline";
  request: VacationRequest;
};

export function VacationRequestWorkspace({ mode }: { mode: "staff" | "portal" | "review" }) {
  const { message } = AntdApp.useApp();
  const queryClient = useQueryClient();
  const [requestForm] = Form.useForm<RequestFormValues>();
  const [decisionForm] = Form.useForm<DecisionFormValues>();
  const [reviewView, setReviewView] = useState<"pending" | "history">("pending");
  const [decision, setDecision] = useState<DecisionState | null>(null);
  const portal = mode === "portal";
  const review = mode === "review";
  const query = useQuery({
    queryKey: review ? vacationRequestQueryKeys.review(reviewView) : vacationRequestQueryKeys.mine(portal),
    queryFn: ({ signal }) => (review ? vacationRequestsApi.listReview(reviewView, signal) : vacationRequestsApi.listMine(portal, signal)),
  });

  const showErrors = (error: unknown) => {
    for (const errorMessage of getApiErrorMessages(error)) {
      void message.error(errorMessage);
    }
  };
  const invalidate = () => queryClient.invalidateQueries({ queryKey: vacationRequestQueryKeys.all });

  const createMutation = useMutation({
    mutationFn: (values: RequestFormValues) =>
      vacationRequestsApi.create(
        {
          startDate: values.period[0].format("YYYY-MM-DD"),
          endDate: values.period[1].format("YYYY-MM-DD"),
          message: values.message?.trim() || undefined,
        },
        portal,
      ),
    onSuccess: async () => {
      requestForm.resetFields();
      await invalidate();
      void message.success("Заявка отправлена суперпользователю");
    },
    onError: showErrors,
  });

  const cancelMutation = useMutation({
    mutationFn: (request: VacationRequest) => vacationRequestsApi.cancel(request.id, request.version),
    onSuccess: async () => {
      await invalidate();
      void message.success("Заявка отозвана");
    },
    onError: showErrors,
  });

  const decisionMutation = useMutation({
    mutationFn: ({ state, values }: { state: DecisionState; values: DecisionFormValues }) => {
      const input = {
        expectedVersion: state.request.version,
        message: values.message?.trim() || undefined,
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
    onError: showErrors,
  });

  const requests = query.data ?? [];

  return (
    <Space orientation="vertical" size={20} className="wide">
      {!review ? (
        <Card title="Новая заявка">
          <Alert
            type="info"
            showIcon
            className={styles.notice}
            title="Отпуск начнёт влиять на расписание только после одобрения суперпользователем."
          />
          <Form<RequestFormValues>
            form={requestForm}
            layout="vertical"
            requiredMark={false}
            onFinish={(values) => {
              createMutation.mutate(values);
            }}
          >
            <Form.Item name="period" label="Период отпуска" rules={[{ required: true, message: "Укажите период отпуска" }]}>
              <DatePicker.RangePicker className="wide" format="DD.MM.YYYY" />
            </Form.Item>
            <Form.Item name="message" label="Комментарий" rules={[{ max: 500, message: "Не больше 500 символов" }]}>
              <Input.TextArea rows={3} maxLength={500} showCount placeholder="Необязательное сообщение для суперпользователя" />
            </Form.Item>
            <Button type="primary" htmlType="submit" loading={createMutation.isPending}>
              Отправить заявку
            </Button>
          </Form>
        </Card>
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
            title="Есть конфликты расписания. Система повторно проверит период и не изменит занятия автоматически."
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
  return `${formatDate(startDate)} — ${formatDate(endDate)}`;
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat("ru-RU").format(new Date(`${value}T00:00:00`));
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat("ru-RU", { dateStyle: "medium", timeStyle: "short" }).format(new Date(value));
}
