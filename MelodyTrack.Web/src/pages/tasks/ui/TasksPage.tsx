import {
  Button,
  Card,
  DatePicker,
  Empty,
  Form,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Radio,
  Select,
  Space,
  Switch,
  Tabs,
  Tag,
  Typography,
} from "antd";
import type { TextAreaRef } from "antd/es/input/TextArea";
import type { Dayjs } from "dayjs";
import { useRef } from "react";

import { ClientSelect } from "@/entities/client";
import { getPhoneUri, getSocialHandle } from "@/entities/client";
import type { RecurringTask, RecurringTaskListStatus, RecurringTaskRule, RecurringTaskType } from "@/entities/task";
import { getRecurringTaskTypeLabel } from "@/entities/task";
import { formatRecordActivitySummary } from "@/shared/lib";
import type { DurableFormStatus } from "@/shared/lib/react";
import { CopyTextModal, DraftFormModal, PageLayout } from "@/shared/ui";
import {
  CalendarCheckOutlined,
  CheckOutlined,
  ClockCircleOutlined,
  CloseOutlined,
  CopyOutlined,
  DownloadOutlined,
  EditOutlined,
  LinkOutlined,
  PhoneOutlined,
  PlusOutlined,
  SendOutlined,
} from "@/shared/ui/icons";

import { type CustomTaskFormValues, type RecurringTaskRuleFormValues, useTasksPageController } from "../model/useTasksPageController";
import { PreparedScheduleShareModal } from "./PreparedScheduleShareModal";

const statusOptions: { label: string; value: RecurringTaskListStatus }[] = [
  { label: "Открытые", value: "open" },
  { label: "Завершённые", value: "completed" },
  { label: "Отменённые", value: "cancelled" },
  { label: "Отложенные", value: "delayed" },
];

const typeOptions: { label: string; value: RecurringTaskType | "all" }[] = [
  { label: "Все типы", value: "all" },
  { label: "Напоминания о записи", value: "appointment-reminder" },
  { label: "Поздравления с днём рождения", value: "birthday-greeting" },
  { label: "После пробного занятия", value: "trial-follow-up" },
  { label: "Вернуть клиента", value: "inactive-client-reminder" },
  { label: "Расписание преподавателя", value: "teacher-daily-schedule" },
  { label: "Напоминания о долге", value: "debtor-reminder" },
  { label: "Пользовательские задачи", value: "custom-task" },
];

export function TasksPage() {
  const controller = useTasksPageController();

  return (
    <PageLayout
      title="Задачи"
      actions={
        controller.activeTab === "tasks" ? (
          <Button type="primary" icon={<PlusOutlined />} onClick={controller.openCustomTaskModal}>
            Добавить задачу
          </Button>
        ) : undefined
      }
    >
      <Tabs
        data-onboarding-id="tasks-content"
        activeKey={controller.activeTab}
        onChange={(key) => {
          controller.setActiveTab(key as "tasks" | "rules");
        }}
        items={[
          {
            key: "tasks",
            label: "Задачи",
            children: (
              <Space orientation="vertical" size={16} className="wide">
                <Card>
                  <Space wrap size={12}>
                    <Select
                      value={controller.status}
                      options={statusOptions}
                      style={{ minWidth: 200 }}
                      onChange={(value) => {
                        controller.setStatus(value);
                      }}
                    />
                    <Select
                      value={controller.type}
                      options={typeOptions}
                      style={{ minWidth: 280 }}
                      onChange={(value) => {
                        controller.setType(value);
                      }}
                    />
                  </Space>
                </Card>

                {controller.tasks.length === 0 && !controller.query.isLoading ? (
                  <Card>
                    <Empty image={<CalendarCheckOutlined size={28} />} description={getEmptyDescription(controller.status)} />
                  </Card>
                ) : null}

                {controller.tasks.map((task) => (
                  <TaskCard
                    key={task.deduplicationKey}
                    task={task}
                    completePending={
                      controller.completeMutation.isPending &&
                      controller.completeMutation.variables.deduplicationKey === task.deduplicationKey
                    }
                    cancelPending={
                      controller.cancelMutation.isPending && controller.cancelMutation.variables.deduplicationKey === task.deduplicationKey
                    }
                    delayPending={controller.delayMutation.isPending && controller.delayingTask?.deduplicationKey === task.deduplicationKey}
                    listStatus={controller.status}
                    onComplete={() => controller.completeTask(task)}
                    onCancel={() => controller.cancelTask(task)}
                    onDelay={() => {
                      controller.openDelayTask(task);
                    }}
                    onOpenTelegram={() => {
                      void controller.openTaskTelegram(task);
                    }}
                    onOpenVk={() => {
                      void controller.openTaskVk(task);
                    }}
                    onCopyMessage={() => {
                      void controller.copyTaskPreparedMessage(task);
                    }}
                    onDownloadSchedule={() => {
                      void controller.downloadTeacherSchedule(task);
                    }}
                    onOpenTeacherScheduleMessenger={(messengerUrl) => {
                      void controller.openTeacherScheduleMessenger(task, messengerUrl);
                    }}
                  />
                ))}
              </Space>
            ),
          },
          {
            key: "rules",
            label: "Правила",
            children: (
              <Space orientation="vertical" size={16} className="wide">
                {controller.rules.length === 0 && !controller.rulesQuery.isLoading ? (
                  <Card>
                    <Empty image={<CalendarCheckOutlined size={28} />} description="Правил пока нет" />
                  </Card>
                ) : null}

                {controller.rules.map((rule) => (
                  <RuleCard
                    key={rule.id}
                    rule={rule}
                    onEdit={() => {
                      controller.openRuleEditor(rule);
                    }}
                  />
                ))}
              </Space>
            ),
          },
        ]}
      />
      <RuleEditorModal
        open={Boolean(controller.editingRule)}
        form={controller.ruleForm}
        rule={controller.currentEditingRule}
        savePending={controller.updateRuleMutation.isPending}
        isStale={controller.isEditingRuleStale}
        onCancel={controller.closeRuleEditor}
        onSubmit={controller.submitRuleEditor}
        draftStatus={controller.ruleDraft.status}
        draftRestored={controller.ruleDraft.restored}
        hasDraft={controller.ruleDraft.hasDraft}
        onDiscardDraft={() => {
          void controller.ruleDraft.discard().then(() => {
            if (controller.editingRule) controller.openRuleEditor(controller.editingRule);
          });
        }}
        onValuesChange={controller.onRuleValuesChange}
        draftStale={controller.ruleDraft.isStale}
        onReapplyDraft={controller.ruleDraft.reapply}
        onRetryDraft={controller.ruleDraft.retry}
      />
      <DelayTaskModal
        open={Boolean(controller.delayingTask)}
        task={controller.delayingTask}
        form={controller.delayTaskForm}
        savePending={controller.delayMutation.isPending}
        onCancel={controller.closeDelayTask}
        onSubmit={controller.submitDelayTask}
      />
      <CustomTaskModal
        open={controller.isCustomTaskModalOpen}
        form={controller.customTaskForm}
        savePending={controller.createCustomTaskMutation.isPending}
        onCancel={controller.closeCustomTaskModal}
        onSubmit={controller.submitCustomTask}
        draftStatus={controller.customTaskDraft.status}
        draftRestored={controller.customTaskDraft.restored}
        hasDraft={controller.customTaskDraft.hasDraft}
        onDiscardDraft={() => {
          void controller.customTaskDraft.discard().then(() => {
            controller.customTaskForm.resetFields();
          });
        }}
        onValuesChange={controller.onCustomTaskValuesChange}
        onRetryDraft={controller.customTaskDraft.retry}
      />
      <CopyTextModal {...controller.copyTextModalProps} />
      <PreparedScheduleShareModal content={controller.preparedScheduleShare} onClose={controller.closePreparedScheduleShare} />
    </PageLayout>
  );
}

type DelayTaskModalValues = {
  delayUntil: Dayjs;
};

function TaskCard({
  task,
  completePending,
  cancelPending,
  delayPending,
  listStatus,
  onComplete,
  onCancel,
  onDelay,
  onOpenTelegram,
  onOpenVk,
  onCopyMessage,
  onDownloadSchedule,
  onOpenTeacherScheduleMessenger,
}: {
  task: RecurringTask;
  completePending: boolean;
  cancelPending: boolean;
  delayPending: boolean;
  listStatus: RecurringTaskListStatus;
  onComplete: () => Promise<unknown>;
  onCancel: () => Promise<unknown>;
  onDelay: () => void;
  onOpenTelegram: () => void;
  onOpenVk: () => void;
  onCopyMessage: () => void;
  onDownloadSchedule: () => void;
  onOpenTeacherScheduleMessenger: (messengerUrl: string) => void;
}) {
  const hasTelegram = Boolean(task.telegram);
  const hasVk = Boolean(task.vk);
  const isOpenTask = listStatus === "open";
  const isTeacherSchedule = task.type === "teacher-daily-schedule";

  return (
    <Card loading={completePending || cancelPending || delayPending}>
      <Space orientation="vertical" size={12} className="wide">
        <Space align="start" className="wide" style={{ justifyContent: "space-between" }} wrap>
          <div>
            <Typography.Title level={5} style={{ margin: 0 }}>
              {task.title}
            </Typography.Title>
            <Typography.Text strong>{task.relatedPersonDisplayName}</Typography.Text>
          </div>
          <Space orientation="vertical" size={8} style={{ alignItems: "flex-end" }}>
            <Space wrap style={{ justifyContent: "flex-end" }}>
              <Tag>{getTypeLabel(task.type)}</Tag>
              {task.relevantAtUtc ? (
                <Tag>{formatRelevantDate(task.relevantAtUtc)}</Tag>
              ) : (
                <Tag>{formatBusinessDate(task.businessDate)}</Tag>
              )}
              {task.delayedUntilUtc ? <Tag color="processing">До: {formatRelevantDateTime(task.delayedUntilUtc)}</Tag> : null}
              {isOpenTask ? null : <Tag color={listStatus === "completed" ? "success" : "default"}>{getStatusLabel(listStatus)}</Tag>}
            </Space>
          </Space>
        </Space>

        <div style={{ display: "flex", justifyContent: "space-between", gap: 12, flexWrap: "wrap" }}>
          <Space wrap>
            {task.phone && !isTeacherSchedule ? (
              <Button icon={<PhoneOutlined />} href={getPhoneUri(task.phone)}>
                Позвонить
              </Button>
            ) : null}
            {hasTelegram ? (
              <Button
                icon={<SendOutlined />}
                onClick={
                  isTeacherSchedule
                    ? () => {
                        if (task.telegram) {
                          onOpenTeacherScheduleMessenger(buildTelegramDeepLink(task.telegram));
                        }
                      }
                    : onOpenTelegram
                }
              >
                Telegram
              </Button>
            ) : null}
            {hasVk ? (
              <Button
                icon={<LinkOutlined />}
                onClick={() => {
                  if (isTeacherSchedule) {
                    if (task.vk) {
                      onOpenTeacherScheduleMessenger(buildVkLink(task.vk));
                    }
                    return;
                  }

                  onOpenVk();
                }}
              >
                VK
              </Button>
            ) : null}
            <Button icon={<CopyOutlined />} onClick={onCopyMessage}>
              Скопировать текст
            </Button>
            {task.type === "teacher-daily-schedule" && task.teacherId ? (
              <Button icon={<DownloadOutlined />} onClick={onDownloadSchedule}>
                Скачать PNG
              </Button>
            ) : null}
          </Space>
          {isOpenTask ? (
            <Space wrap>
              <Popconfirm
                title="Завершить задачу?"
                description="Задача будет отмечена как выполненная и больше не появится в этом периоде."
                okText="Завершить"
                cancelText="Отмена"
                okButtonProps={{ loading: completePending }}
                onConfirm={onComplete}
              >
                <Button type="primary" icon={<CheckOutlined />} loading={completePending}>
                  Завершить
                </Button>
              </Popconfirm>
              <Button icon={<ClockCircleOutlined />} loading={delayPending} onClick={onDelay}>
                Отложить
              </Button>
              <Popconfirm
                title="Отменить задачу?"
                description="Задача будет отменена и больше не появится в этом периоде."
                okText="Отменить"
                cancelText="Отмена"
                okButtonProps={{ danger: true, loading: cancelPending }}
                onConfirm={onCancel}
              >
                <Button icon={<CloseOutlined />} loading={cancelPending}>
                  Отменить
                </Button>
              </Popconfirm>
            </Space>
          ) : null}
        </div>
      </Space>
    </Card>
  );
}

function RuleCard({ rule, onEdit }: { rule: RecurringTaskRule; onEdit: () => void }) {
  return (
    <Card
      extra={
        <Button icon={<EditOutlined />} onClick={onEdit}>
          Редактировать
        </Button>
      }
    >
      <Space orientation="vertical" size={12} className="wide">
        <Space align="start" className="wide" style={{ justifyContent: "space-between" }} wrap>
          <div>
            <Typography.Title level={5} style={{ margin: 0 }}>
              {rule.name}
            </Typography.Title>
            <Typography.Text type="secondary">{getTypeLabel(rule.type)}</Typography.Text>
          </div>
          <Space wrap>
            <Tag color={rule.isEnabled ? "success" : "default"}>{rule.isEnabled ? "Включено" : "Выключено"}</Tag>
            {rule.offsetMinutes != null ? <Tag>Смещение: {rule.offsetMinutes} мин</Tag> : null}
            {rule.cooldownDays != null ? <Tag>Повтор: {rule.cooldownDays} дн</Tag> : null}
          </Space>
        </Space>
        <Typography.Paragraph style={{ marginBottom: 0 }}>{rule.messageTemplate}</Typography.Paragraph>
      </Space>
    </Card>
  );
}

function RuleEditorModal({
  open,
  form,
  rule,
  savePending,
  isStale,
  onCancel,
  onSubmit,
  draftStatus,
  draftRestored,
  hasDraft,
  onDiscardDraft,
  onValuesChange,
  draftStale,
  onReapplyDraft,
  onRetryDraft,
}: {
  open: boolean;
  form: ReturnType<typeof Form.useForm<RecurringTaskRuleFormValues>>[0];
  rule?: RecurringTaskRule | null;
  savePending: boolean;
  isStale: boolean;
  onCancel: () => void;
  onSubmit: (values: RecurringTaskRuleFormValues) => void;
  draftStatus: DurableFormStatus;
  draftRestored: boolean;
  hasDraft: boolean;
  onDiscardDraft: () => void;
  onValuesChange?: (_: Partial<RecurringTaskRuleFormValues>, values: RecurringTaskRuleFormValues) => void;
  draftStale: boolean;
  onReapplyDraft: () => void;
  onRetryDraft: () => void;
}) {
  const messageTemplateInputRef = useRef<TextAreaRef>(null);
  const availableTokens = rule ? getAvailableTemplateTokens(rule.type) : [];

  const insertToken = (token: string) => {
    const textarea = messageTemplateInputRef.current?.resizableTextArea?.textArea;
    const currentValue = (form.getFieldValue("messageTemplate") as string | undefined) ?? "";

    if (!textarea) {
      form.setFieldValue("messageTemplate", `${currentValue}${token}`);
      return;
    }

    const selectionStart = textarea.selectionStart;
    const selectionEnd = textarea.selectionEnd;
    const nextValue = `${currentValue.slice(0, selectionStart)}${token}${currentValue.slice(selectionEnd)}`;

    form.setFieldValue("messageTemplate", nextValue);

    requestAnimationFrame(() => {
      const nextCursorPosition = selectionStart + token.length;
      textarea.focus();
      textarea.setSelectionRange(nextCursorPosition, nextCursorPosition);
    });
  };

  return (
    <DraftFormModal
      open={open}
      title={rule ? `Правило: ${rule.name}` : "Правило"}
      restored={draftRestored}
      saveStatus={draftStatus}
      showClearDraft={hasDraft}
      onClearDraft={onDiscardDraft}
      stale={draftStale}
      onReapplyDraft={onReapplyDraft}
      onRetryDraft={onRetryDraft}
      onCancel={onCancel}
      onOk={() => {
        form.submit();
      }}
      confirmLoading={savePending}
      okText="Сохранить"
      cancelText="Отмена"
    >
      <Form form={form} layout="vertical" requiredMark={false} onFinish={onSubmit} onValuesChange={onValuesChange}>
        <Space orientation="vertical" size={12} className="wide">
          {isStale && rule?.lastActivity ? (
            <Typography.Text type="warning">{formatRecordActivitySummary(rule.lastActivity)}</Typography.Text>
          ) : null}
          <Form.Item name="isEnabled" label="Включено" valuePropName="checked">
            <Switch />
          </Form.Item>
          <Form.Item name="messageTemplate" label="Шаблон сообщения" rules={[{ required: true }]}>
            <Input.TextArea ref={messageTemplateInputRef} rows={5} />
          </Form.Item>
          {availableTokens.length > 0 ? (
            <Space orientation="vertical" size={8} className="wide">
              <Typography.Text type="secondary">Доступные шаблоны для быстрой вставки:</Typography.Text>
              <Space wrap>
                {availableTokens.map((item) => (
                  <Button
                    key={item.token}
                    size="small"
                    onClick={() => {
                      insertToken(item.token);
                    }}
                  >
                    {item.token}
                  </Button>
                ))}
              </Space>
              <Space orientation="vertical" size={4} className="wide">
                {availableTokens.map((item) => (
                  <Typography.Text key={`${item.token}:hint`} type="secondary">
                    <strong>{item.token}</strong>: {item.description}
                  </Typography.Text>
                ))}
              </Space>
            </Space>
          ) : null}
          {rule && supportsOffsetMinutes(rule.type) ? (
            <Form.Item name="offsetMinutes" label="Смещение, минут" rules={[{ type: "number", min: 1 }]}>
              <InputNumber min={1} step={15} className="wide" />
            </Form.Item>
          ) : null}
          {rule && supportsCooldownDays(rule.type) ? (
            <Form.Item name="cooldownDays" label="Период повтора, дней" rules={[{ type: "number", min: 1 }]}>
              <InputNumber min={1} className="wide" />
            </Form.Item>
          ) : null}
        </Space>
      </Form>
    </DraftFormModal>
  );
}

function DelayTaskModal({
  open,
  task,
  form,
  savePending,
  onCancel,
  onSubmit,
}: {
  open: boolean;
  task?: RecurringTask | null;
  form: ReturnType<typeof Form.useForm<DelayTaskModalValues>>[0];
  savePending: boolean;
  onCancel: () => void;
  onSubmit: (values: DelayTaskModalValues) => void | Promise<void>;
}) {
  return (
    <Modal
      open={open}
      title={task ? `Отложить: ${task.title}` : "Отложить задачу"}
      onCancel={onCancel}
      onOk={() => {
        form.submit();
      }}
      confirmLoading={savePending}
      okText="Отложить"
      cancelText="Отмена"
    >
      <Form form={form} layout="vertical" requiredMark={false} onFinish={onSubmit}>
        <Form.Item
          name="delayUntil"
          label="Новая дата и время"
          rules={[{ required: true, message: "Выберите дату и время." }]}
          extra="Задача снова появится в списке открытых в указанное время."
        >
          <DatePicker showTime={{ format: "HH:mm" }} format="DD.MM.YYYY HH:mm" className="wide" />
        </Form.Item>
      </Form>
    </Modal>
  );
}

function CustomTaskModal({
  open,
  form,
  savePending,
  onCancel,
  onSubmit,
  draftStatus,
  draftRestored,
  hasDraft,
  onDiscardDraft,
  onValuesChange,
  onRetryDraft,
}: {
  open: boolean;
  form: ReturnType<typeof Form.useForm<CustomTaskFormValues>>[0];
  savePending: boolean;
  onCancel: () => void;
  onSubmit: (values: CustomTaskFormValues) => void | Promise<void>;
  draftStatus: DurableFormStatus;
  draftRestored: boolean;
  hasDraft: boolean;
  onDiscardDraft: () => void;
  onValuesChange?: (_: Partial<CustomTaskFormValues>, values: CustomTaskFormValues) => void;
  onRetryDraft: () => void;
}) {
  const recipientMode = Form.useWatch("recipientMode", form);

  return (
    <DraftFormModal
      open={open}
      title="Новая задача"
      restored={draftRestored}
      saveStatus={draftStatus}
      showClearDraft={hasDraft}
      onClearDraft={onDiscardDraft}
      onRetryDraft={onRetryDraft}
      onCancel={onCancel}
      onOk={() => {
        form.submit();
      }}
      confirmLoading={savePending}
      okText="Создать"
      cancelText="Отмена"
      destroyOnHidden
    >
      <Form
        form={form}
        layout="vertical"
        requiredMark={false}
        initialValues={{ recipientMode: "client" }}
        onFinish={onSubmit}
        onValuesChange={onValuesChange}
      >
        <Form.Item name="recipientMode" label="Кому задача" rules={[{ required: true }]}>
          <Radio.Group
            optionType="button"
            buttonStyle="solid"
            options={[
              { label: "Существующий клиент", value: "client" },
              { label: "Не клиент", value: "external" },
            ]}
          />
        </Form.Item>

        {recipientMode !== "external" ? (
          <Form.Item name="clientId" label="Клиент" rules={[{ required: true, message: "Выберите клиента." }]}>
            <ClientSelect />
          </Form.Item>
        ) : (
          <>
            <Form.Item name="recipientName" label="Имя получателя" rules={[{ required: true, message: "Укажите имя получателя." }]}>
              <Input />
            </Form.Item>
            <Form.Item name="phone" label="Телефон">
              <Input />
            </Form.Item>
            <Form.Item name="telegram" label="Telegram">
              <Input />
            </Form.Item>
            <Form.Item name="vk" label="VK">
              <Input />
            </Form.Item>
          </>
        )}

        <Form.Item name="title" label="Заголовок" rules={[{ required: true, message: "Укажите заголовок задачи." }]}>
          <Input />
        </Form.Item>
        <Form.Item name="dueAt" label="Когда показать задачу" rules={[{ required: true, message: "Выберите дату и время." }]}>
          <DatePicker showTime={{ format: "HH:mm" }} format="DD.MM.YYYY HH:mm" className="wide" />
        </Form.Item>
        <Form.Item name="messageText" label="Текст задачи" rules={[{ required: true, message: "Укажите текст задачи." }]}>
          <Input.TextArea rows={5} />
        </Form.Item>
      </Form>
    </DraftFormModal>
  );
}

function getTypeLabel(type: RecurringTaskType) {
  return getRecurringTaskTypeLabel(type);
}

function getStatusLabel(status: RecurringTaskListStatus) {
  switch (status) {
    case "completed":
      return "Завершена";
    case "cancelled":
      return "Отменена";
    case "delayed":
      return "Отложена";
    case "open":
      return "Открыта";
  }
}

function supportsOffsetMinutes(type: RecurringTaskType) {
  return type === "appointment-reminder" || type === "trial-follow-up" || type === "debtor-reminder";
}

function supportsCooldownDays(type: RecurringTaskType) {
  return (
    type === "birthday-greeting" || type === "inactive-client-reminder" || type === "teacher-daily-schedule" || type === "debtor-reminder"
  );
}

function getAvailableTemplateTokens(type: RecurringTaskType) {
  const clientTokens = [
    { token: "{Client.FirstName}", description: "Имя клиента" },
    { token: "{Client.LastName}", description: "Фамилия клиента" },
    { token: "{Client.Patronymic}", description: "Отчество клиента" },
  ];

  switch (type) {
    case "appointment-reminder":
      return [
        ...clientTokens,
        { token: "{When}", description: "Слово вроде «сегодня» или «завтра»" },
        { token: "{Appointment.StartTime}", description: "Время начала записи" },
        { token: "{Appointment.Date}", description: "Дата записи" },
        { token: "{Date}", description: "Дата записи" },
      ];
    case "birthday-greeting":
      return [...clientTokens, { token: "{Date}", description: "Текущая бизнес-дата" }];
    case "trial-follow-up":
      return [...clientTokens, { token: "{Date}", description: "Дата формирования задачи" }];
    case "inactive-client-reminder":
      return [...clientTokens, { token: "{Date}", description: "Дата текущего периода напоминания" }];
    case "teacher-daily-schedule":
      return [
        { token: "{Teacher.FirstName}", description: "Имя преподавателя" },
        { token: "{Teacher.LastName}", description: "Фамилия преподавателя" },
        { token: "{Date}", description: "Дата расписания" },
      ];
    case "debtor-reminder":
      return [...clientTokens, { token: "{Date}", description: "Дата текущего напоминания" }];
    case "custom-task":
      return [];
  }
}

function getEmptyDescription(status: RecurringTaskListStatus) {
  switch (status) {
    case "completed":
      return "Завершённых задач пока нет";
    case "cancelled":
      return "Отменённых задач пока нет";
    case "delayed":
      return "Отложенных задач пока нет";
    case "open":
      return "Открытых задач пока нет";
  }
}

function formatRelevantDate(value: string) {
  const formatter = new Intl.DateTimeFormat("ru-RU", {
    day: "2-digit",
    month: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });

  return formatter.format(new Date(value));
}

function formatRelevantDateTime(value: string) {
  return new Intl.DateTimeFormat("ru-RU", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  }).format(new Date(value));
}

function formatBusinessDate(value: string) {
  const date = new Date(`${value}T12:00:00`);
  return new Intl.DateTimeFormat("ru-RU", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  }).format(date);
}

function buildTelegramDeepLink(value: string) {
  const handle = getSocialHandle(value, "telegram");
  return handle ? `tg://resolve?domain=${encodeURIComponent(handle)}` : "";
}

function buildVkLink(value: string) {
  const handle = getSocialHandle(value, "vk");
  return handle ? `https://vk.me/${handle}` : "";
}
