import type { FormInstance, FormProps } from "antd";
import { Button, Checkbox, DatePicker, Form, Input, Modal, Select, Space, Typography } from "antd";
import type { DefaultOptionType } from "antd/es/select";
import dayjs, { type Dayjs } from "dayjs";
import { type ReactNode, useEffect, useRef, useState } from "react";

import type { Appointment, AppointmentMutationScope, AppointmentStatus, RecurrenceType } from "@/entities/appointment";
import { getAppointmentStatusLabel } from "@/entities/appointment";
import { ClientSelect } from "@/entities/client";
import { getPhoneUri, getSocialLinkHref } from "@/entities/client";
import { ServiceSelect } from "@/entities/service";
import { UserSelect } from "@/entities/user";
import { DATE_FORMAT, DATE_TIME_FORMAT, formatDateTime, TIME_FORMAT } from "@/shared/lib";
import { formatRecordActivitySummary } from "@/shared/lib";
import type { DurableFormStatus } from "@/shared/lib/react";
import { DraftFormModal, StatusBanner } from "@/shared/ui";
import {
  CheckOutlined,
  ClockCircleOutlined,
  CloseOutlined,
  DeleteOutlined,
  DollarOutlined,
  EditOutlined,
  FireOutlined,
  LinkOutlined,
  PhoneOutlined,
  SendOutlined,
} from "@/shared/ui/icons";

import styles from "./ScheduleModals.module.css";

const weeklyDayOptions: { label: string; value: number }[] = [
  { label: "Пн", value: 1 },
  { label: "Вт", value: 2 },
  { label: "Ср", value: 4 },
  { label: "Чт", value: 8 },
  { label: "Пт", value: 16 },
  { label: "Сб", value: 32 },
  { label: "Вс", value: 64 },
];

export type AppointmentFormValues = {
  clientId: string;
  serviceId: string;
  providerId?: string;
  courseThemeId?: string;
  lessonNotes?: string;
  startDate: Dayjs | null;
  recurrenceTypeId?: string;
  patternEndDate?: Dayjs;
  weeklyDays?: number[];
};

export type AppointmentEditFormValues = {
  clientId: string;
  serviceId: string;
  providerId?: string;
  courseThemeId?: string;
  lessonNotes?: string;
  startDate: Dayjs;
};

type RecurringDeleteOption = {
  scope: AppointmentMutationScope;
  label: string;
  description: string;
};

const DELETE_CONFIRMATION_TIMEOUT_MS = 3000;

export function AppointmentEditModal({
  appointment,
  createdClientOptions,
  editPending,
  form,
  isStale,
  lockedProviderId,
  courseThemeOptions,
  courseThemesLoading,
  canCreateClient = true,
  onCreateClient,
  onCancel,
  onSubmit,
  onValuesChange,
  draftStatus,
  draftRestored,
  hasDraft,
  onDiscardDraft,
  draftStale,
  onReapplyDraft,
  onRetryDraft,
}: {
  appointment: Appointment | null;
  createdClientOptions: DefaultOptionType[];
  editPending: boolean;
  form: FormInstance<AppointmentEditFormValues>;
  isStale: boolean;
  lockedProviderId?: string;
  courseThemeOptions: DefaultOptionType[];
  courseThemesLoading: boolean;
  canCreateClient?: boolean;
  onCreateClient: () => void;
  onCancel: () => void;
  onSubmit: (values: AppointmentEditFormValues) => void;
  onValuesChange?: FormProps<AppointmentEditFormValues>["onValuesChange"];
  draftStatus: DurableFormStatus;
  draftRestored: boolean;
  hasDraft: boolean;
  onDiscardDraft: () => void;
  draftStale: boolean;
  onReapplyDraft: () => void;
  onRetryDraft: () => void;
}) {
  useEffect(() => {
    if (!appointment) {
      form.resetFields();
      return;
    }

    form.setFieldsValue({
      clientId: appointment.client.id,
      serviceId: appointment.service.id,
      providerId: lockedProviderId ?? appointment.provider?.id,
      courseThemeId: appointment.courseTheme?.id,
      lessonNotes: appointment.lessonNotes ?? undefined,
      startDate: dayjs(appointment.startDate),
    });
  }, [appointment, form, lockedProviderId]);

  return (
    <DraftFormModal
      open={appointment !== null}
      title="Редактировать запись"
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
      confirmLoading={editPending}
      destroyOnHidden
    >
      {appointment ? (
        <Form<AppointmentEditFormValues>
          form={form}
          layout="vertical"
          requiredMark={false}
          onFinish={onSubmit}
          onValuesChange={onValuesChange}
        >
          {isStale ? (
            <StatusBanner
              type="warning"
              title="Запись изменилась в другом окне"
              description={formatRecordActivitySummary(appointment.lastActivity)}
            />
          ) : null}
          <Form.Item label="Клиент">
            <Space orientation="vertical" size={8} className="wide">
              <Form.Item name="clientId" noStyle rules={[{ required: true }]}>
                <ClientSelect extraOptions={createdClientOptions} />
              </Form.Item>
              {canCreateClient ? <Button onClick={onCreateClient}>Новый клиент</Button> : null}
            </Space>
          </Form.Item>
          <Form.Item name="serviceId" label="Услуга" rules={[{ required: true }]}>
            <ServiceSelect allowClear={false} showPrice />
          </Form.Item>
          <Form.Item name="providerId" label="Преподаватель">
            <UserSelect disabled={Boolean(lockedProviderId)} />
          </Form.Item>
          <Form.Item name="courseThemeId" label="Тема занятия">
            <Select
              allowClear
              loading={courseThemesLoading}
              options={courseThemeOptions}
              placeholder="Не выбрана"
              disabled={courseThemeOptions.length === 0}
              showSearch={{
                filterOption: (input, option) =>
                  (typeof option?.label === "string" ? option.label : "").toLowerCase().includes(input.trim().toLowerCase()),
              }}
            />
          </Form.Item>
          <Form.Item name="lessonNotes" label="Заметки по уроку">
            <Input.TextArea
              autoSize={{ minRows: 3, maxRows: 6 }}
              placeholder="Что именно проходили, на чем остановились, что задать дальше"
              maxLength={4000}
            />
          </Form.Item>
          <Form.Item name="startDate" label="Начало" rules={[{ required: true }]}>
            <DatePicker showTime={{ format: TIME_FORMAT }} format={DATE_TIME_FORMAT} className="wide" />
          </Form.Item>
          {appointment.recurringRule ? (
            <Typography.Text type="secondary">
              Изменяются только клиент, услуга, Преподаватель и время. Повторяющаяся серия останется без изменений.
            </Typography.Text>
          ) : null}
        </Form>
      ) : null}
    </DraftFormModal>
  );
}

export function AppointmentCreateModal({
  createPending,
  createdClientOptions,
  hasDraft,
  draftRestored,
  draftSaveStatus,
  form,
  lockedProviderId,
  canCreateClient = true,
  onCreateClient,
  onCancel,
  onDraftChange,
  onClientLabelChange,
  onServiceLabelChange,
  onProviderLabelChange,
  onSubmit,
  open,
  onClearDraft,
  onRetryDraft,
  recurrenceTypes,
  recurrenceTypesLoading,
  courseThemeOptions,
  courseThemesLoading,
}: {
  createPending: boolean;
  createdClientOptions: DefaultOptionType[];
  hasDraft: boolean;
  draftRestored: boolean;
  draftSaveStatus: DurableFormStatus;
  form: FormInstance<AppointmentFormValues>;
  lockedProviderId?: string;
  canCreateClient?: boolean;
  onCreateClient: () => void;
  onCancel: () => void;
  onDraftChange: (values: AppointmentFormValues) => void;
  onClientLabelChange: (label?: string) => void;
  onServiceLabelChange: (label?: string) => void;
  onProviderLabelChange: (label?: string) => void;
  onSubmit: (values: AppointmentFormValues) => void;
  open: boolean;
  onClearDraft: () => void;
  onRetryDraft: () => void;
  recurrenceTypes: RecurrenceType[];
  recurrenceTypesLoading: boolean;
  courseThemeOptions: DefaultOptionType[];
  courseThemesLoading: boolean;
}) {
  const recurrenceTypeId = Form.useWatch("recurrenceTypeId", form);
  const startDate = Form.useWatch("startDate", form);
  const weeklyDays = Form.useWatch("weeklyDays", form);
  const weeklyRecurrenceType = recurrenceTypes.find((item) => item.key === "weekly");
  const recurrenceEnabled = Boolean(recurrenceTypeId);

  useEffect(() => {
    if (!recurrenceTypeId || !weeklyRecurrenceType || recurrenceTypeId === weeklyRecurrenceType.id) {
      return;
    }

    const nextWeeklyDays = weeklyDays?.length ? weeklyDays : [getWeeklyBitmaskValue(startDate)];
    form.setFieldsValue({
      recurrenceTypeId: weeklyRecurrenceType.id,
      weeklyDays: nextWeeklyDays,
    });
  }, [form, recurrenceTypeId, startDate, weeklyDays, weeklyRecurrenceType]);

  useEffect(() => {
    if (!recurrenceEnabled || weeklyDays?.length) {
      return;
    }

    form.setFieldValue("weeklyDays", [getWeeklyBitmaskValue(startDate)]);
  }, [form, recurrenceEnabled, startDate, weeklyDays]);

  const handleRecurrenceEnabledChange = (enabled: boolean) => {
    if (!enabled || !weeklyRecurrenceType) {
      form.setFieldsValue({ patternEndDate: undefined, weeklyDays: undefined });
      form.setFieldValue("recurrenceTypeId", undefined);
      return;
    }

    form.setFieldsValue({
      recurrenceTypeId: weeklyRecurrenceType.id,
      weeklyDays: weeklyDays?.length ? weeklyDays : [getWeeklyBitmaskValue(startDate)],
    });
  };

  return (
    <DraftFormModal
      open={open}
      title="Новая запись"
      restored={draftRestored}
      saveStatus={draftSaveStatus}
      showClearDraft={hasDraft}
      onClearDraft={onClearDraft}
      onRetryDraft={onRetryDraft}
      onCancel={onCancel}
      onOk={() => {
        form.submit();
      }}
      confirmLoading={createPending}
      destroyOnHidden
    >
      <Form<AppointmentFormValues>
        form={form}
        layout="vertical"
        requiredMark={false}
        initialValues={{ startDate: dayjs() }}
        onFinish={onSubmit}
        onValuesChange={(_, values) => {
          onDraftChange(values);
        }}
      >
        <Form.Item name="recurrenceTypeId" hidden>
          <input type="hidden" />
        </Form.Item>
        <Form.Item label="Клиент">
          <Space orientation="vertical" size={8} className="wide">
            <Form.Item name="clientId" noStyle rules={[{ required: true }]}>
              <ClientSelect extraOptions={createdClientOptions} onResolvedLabelChange={onClientLabelChange} />
            </Form.Item>
            {canCreateClient ? <Button onClick={onCreateClient}>Новый клиент</Button> : null}
          </Space>
        </Form.Item>
        <Form.Item name="serviceId" label="Услуга" rules={[{ required: true }]}>
          <ServiceSelect allowClear={false} showPrice onResolvedLabelChange={onServiceLabelChange} />
        </Form.Item>
        <Form.Item name="providerId" label="Преподаватель">
          <UserSelect disabled={Boolean(lockedProviderId)} onResolvedLabelChange={onProviderLabelChange} />
        </Form.Item>
        <Form.Item name="courseThemeId" label="Тема занятия">
          <Select
            allowClear
            loading={courseThemesLoading}
            options={courseThemeOptions}
            placeholder="Не выбрана"
            disabled={courseThemeOptions.length === 0}
            showSearch={{
              filterOption: (input, option) =>
                (typeof option?.label === "string" ? option.label : "").toLowerCase().includes(input.trim().toLowerCase()),
            }}
          />
        </Form.Item>
        <Form.Item name="lessonNotes" label="Заметки по уроку">
          <Input.TextArea autoSize={{ minRows: 3, maxRows: 6 }} placeholder="Кратко зафиксируйте, что было на уроке" maxLength={4000} />
        </Form.Item>
        <Form.Item name="startDate" label="Начало" rules={[{ required: true }]}>
          <DatePicker showTime={{ format: TIME_FORMAT }} format={DATE_TIME_FORMAT} className="wide" />
        </Form.Item>
        <Form.Item label="Повторение">
          <Checkbox
            checked={recurrenceEnabled}
            disabled={recurrenceTypesLoading || !weeklyRecurrenceType}
            onChange={(event) => {
              handleRecurrenceEnabledChange(event.target.checked);
            }}
          >
            Повторять еженедельно
          </Checkbox>
        </Form.Item>
        {recurrenceEnabled ? (
          <>
            <Form.Item
              name="patternEndDate"
              label="Повторять до"
              extra="Необязательно. Без даты окончания серия остается бессрочной: в календаре и статистике учитываются только записи, попавшие в открытый период."
            >
              <DatePicker
                allowClear
                format={DATE_FORMAT}
                className="wide"
                disabledDate={(current) => {
                  return startDate ? current.isBefore(startDate.startOf("day")) : false;
                }}
              />
            </Form.Item>
            <Form.Item
              name="weeklyDays"
              label="Дни недели"
              rules={[
                {
                  validator: (_, value?: number[]) => {
                    if (value?.length) {
                      return Promise.resolve();
                    }
                    return Promise.reject(new Error("Выберите хотя бы один день недели"));
                  },
                },
              ]}
            >
              <Checkbox.Group className={styles.weeklyDays} options={weeklyDayOptions} />
            </Form.Item>
            <div className={styles.recurrenceHint}>
              <Typography.Text type="secondary">{getRecurrenceSummary("weekly", startDate, weeklyDays)}</Typography.Text>
            </div>
          </>
        ) : null}
      </Form>
    </DraftFormModal>
  );
}

export function RecurringDeleteModal({
  appointment,
  deletePending,
  isStale,
  onCancel,
  onDelete,
}: {
  appointment: Appointment | null;
  deletePending: boolean;
  isStale: boolean;
  onCancel: () => void;
  onDelete: (appointment: Appointment, scope: AppointmentMutationScope) => void;
}) {
  const deleteOptions = appointment ? getRecurringDeleteOptions(appointment) : [];
  const [confirmScope, setConfirmScope] = useState<AppointmentMutationScope | null>(null);
  const confirmTimeoutRef = useRef<number | null>(null);

  useEffect(() => {
    return () => {
      if (confirmTimeoutRef.current !== null) {
        window.clearTimeout(confirmTimeoutRef.current);
      }
    };
  }, []);

  const armDeleteConfirmation = (scope: AppointmentMutationScope) => {
    setConfirmScope(scope);
    if (confirmTimeoutRef.current !== null) {
      window.clearTimeout(confirmTimeoutRef.current);
    }

    confirmTimeoutRef.current = window.setTimeout(() => {
      setConfirmScope(null);
      confirmTimeoutRef.current = null;
    }, DELETE_CONFIRMATION_TIMEOUT_MS);
  };

  const handleDeleteClick = (currentAppointment: Appointment, scope: AppointmentMutationScope) => {
    if (confirmScope === scope) {
      if (confirmTimeoutRef.current !== null) {
        window.clearTimeout(confirmTimeoutRef.current);
        confirmTimeoutRef.current = null;
      }
      setConfirmScope(null);
      onDelete(currentAppointment, scope);
      return;
    }

    armDeleteConfirmation(scope);
  };

  return (
    <Modal
      open={appointment !== null}
      title="Удалить повторяющуюся запись"
      onCancel={deletePending ? undefined : onCancel}
      footer={null}
      destroyOnHidden
    >
      {appointment ? (
        <Space orientation="vertical" size={16} className="wide">
          {isStale ? (
            <StatusBanner
              type="warning"
              title="Запись изменилась в другом окне"
              description={formatRecordActivitySummary(appointment.lastActivity)}
            />
          ) : null}
          <Typography.Text>Выберите, как удалить запись на {formatDateTime(dayjs(appointment.startDate))}.</Typography.Text>
          {isMultiDayWeeklyRecurringAppointment(appointment) ? (
            <Typography.Text type="secondary">
              Серия повторяется по дням: {formatWeeklyPattern(appointment.recurringRule?.recurrencePattern)}. Если выбрать удаление серии,
              действие затронет все эти дни, а не только {getWeekdayLabel(dayjs(appointment.startDate))}.
            </Typography.Text>
          ) : null}
          <Space orientation="vertical" size={10} className={`wide ${styles.recurringDeleteActions}`}>
            {deleteOptions.map((option) => (
              <Button
                key={option.scope}
                danger
                block
                loading={deletePending}
                className={`${styles.recurringActionButton} ${confirmScope === option.scope ? styles.recurringActionButtonConfirm : ""}`}
                onClick={() => {
                  handleDeleteClick(appointment, option.scope);
                }}
              >
                <span className={styles.recurringActionContent}>
                  <span className={styles.recurringActionLabel}>{confirmScope === option.scope ? "Точно?" : option.label}</span>
                  <span className={styles.recurringActionDescription}>{option.description}</span>
                </span>
              </Button>
            ))}
            <Button block disabled={deletePending} onClick={onCancel}>
              Отмена
            </Button>
          </Space>
        </Space>
      ) : null}
    </Modal>
  );
}

export function RecurringRescheduleModal({
  appointment,
  nextStartDate,
  reschedulePending,
  isStale,
  onCancel,
  onReschedule,
}: {
  appointment: Appointment | null;
  nextStartDate: Dayjs | null;
  reschedulePending: boolean;
  isStale: boolean;
  onCancel: () => void;
  onReschedule: (appointment: Appointment, nextStartDate: Dayjs, scope: AppointmentMutationScope) => void;
}) {
  return (
    <Modal
      open={appointment !== null && nextStartDate !== null}
      title="Перенести повторяющуюся запись"
      onCancel={reschedulePending ? undefined : onCancel}
      footer={null}
      destroyOnHidden
    >
      {appointment && nextStartDate ? (
        <Space orientation="vertical" size={16} className="wide">
          {isStale ? (
            <StatusBanner
              type="warning"
              title="Запись изменилась в другом окне"
              description={formatRecordActivitySummary(appointment.lastActivity)}
            />
          ) : null}
          <Typography.Text>
            Выберите, как перенести запись с {formatDateTime(dayjs(appointment.startDate))} на {formatDateTime(nextStartDate)}.
          </Typography.Text>
          <Space orientation="vertical" size={10} className={`wide ${styles.recurringDeleteActions}`}>
            <Button
              block
              loading={reschedulePending}
              onClick={() => {
                onReschedule(appointment, nextStartDate, "single");
              }}
            >
              Только эту запись
            </Button>
            <Button
              block
              loading={reschedulePending}
              onClick={() => {
                onReschedule(appointment, nextStartDate, "this-and-following");
              }}
            >
              Эту и следующие
            </Button>
            <Button
              block
              loading={reschedulePending}
              onClick={() => {
                onReschedule(appointment, nextStartDate, "all");
              }}
            >
              Все записи
            </Button>
            <Button block disabled={reschedulePending} onClick={onCancel}>
              Отмена
            </Button>
          </Space>
        </Space>
      ) : null}
    </Modal>
  );
}

export function AppointmentDetailsModal({
  appointment,
  isStale,
  onClose,
  onEdit,
  onStatusChange,
  onDelete,
  onCreatePayment,
}: {
  appointment: Appointment | null;
  isStale: boolean;
  onClose: () => void;
  onEdit: (appointment: Appointment) => void;
  onStatusChange: (appointment: Appointment, status: AppointmentStatus) => void;
  onDelete: (appointment: Appointment) => void;
  onCreatePayment?: (appointment: Appointment) => void;
}) {
  if (!appointment) {
    return null;
  }

  const start = dayjs(appointment.startDate);
  const end = dayjs(appointment.endDate);
  const clientName = [appointment.client.lastName, appointment.client.firstName, appointment.client.patronymic].filter(Boolean).join(" ");
  const recurrenceSummary = appointment.recurringRule ? formatRecurringRuleSummary(appointment.recurringRule) : null;
  const telegramHref = getSocialLinkHref(appointment.client.contacts?.telegram, "telegram");
  const vkHref = getSocialLinkHref(appointment.client.contacts?.vk, "vk");

  return (
    <Modal open title="Запись" onCancel={onClose} footer={null}>
      <Space orientation="vertical" size={18} className="wide">
        {isStale ? (
          <StatusBanner
            type="warning"
            title="Запись изменилась в другом окне"
            description={formatRecordActivitySummary(appointment.lastActivity)}
          />
        ) : null}
        <div className={styles.detailsHeader}>
          <div>
            <Typography.Title level={3}>{clientName}</Typography.Title>
            <Typography.Text type="secondary">
              {formatDateTime(start)} - {end.format(TIME_FORMAT)}
            </Typography.Text>
          </div>
          {hasClientContacts(appointment.client.contacts) ? (
            <Space wrap className={styles.contactLinks}>
              {appointment.client.contacts?.phone ? (
                <Button
                  shape="circle"
                  icon={<PhoneOutlined />}
                  href={getPhoneUri(appointment.client.contacts.phone)}
                  title={appointment.client.contacts.phone}
                />
              ) : null}
              {telegramHref ? (
                <Button shape="circle" icon={<SendOutlined />} href={telegramHref} target="_blank" rel="noreferrer" title="Telegram" />
              ) : null}
              {vkHref ? <Button shape="circle" icon={<LinkOutlined />} href={vkHref} target="_blank" rel="noreferrer" title="VK" /> : null}
            </Space>
          ) : null}
        </div>
        <div className={styles.detailsGrid}>
          <div>
            <div className={styles.detailValue}>{appointment.service.name}</div>
            <Typography.Text type="secondary">Услуга</Typography.Text>
          </div>
          {appointment.provider ? (
            <div>
              <div className={styles.detailValue}>
                {appointment.provider.lastName} {appointment.provider.firstName}
              </div>
              <Typography.Text type="secondary">Преподаватель</Typography.Text>
            </div>
          ) : null}
          {appointment.courseTheme ? (
            <div>
              <div className={styles.detailValue}>
                {appointment.courseTheme.courseName}: {appointment.courseTheme.title}
              </div>
              <Typography.Text type="secondary">Тема занятия</Typography.Text>
            </div>
          ) : null}
          <div>
            <div className={styles.detailValue}>
              <Select
                value={appointment.status}
                options={appointmentStatusOptions}
                popupMatchSelectWidth={false}
                onChange={(status) => {
                  if (status !== appointment.status) {
                    onStatusChange(appointment, status);
                  }
                }}
              />
            </div>
            <Typography.Text type="secondary">Статус</Typography.Text>
          </div>
          {recurrenceSummary ? (
            <div>
              <div className={styles.detailValue}>{recurrenceSummary}</div>
              <Typography.Text type="secondary">Повторение</Typography.Text>
            </div>
          ) : null}
        </div>
        {appointment.lessonNotes ? (
          <div>
            <Typography.Text strong>Заметки по уроку</Typography.Text>
            <Typography.Paragraph className={styles.notesBlock}>{appointment.lessonNotes}</Typography.Paragraph>
          </div>
        ) : null}
        <Space wrap>
          <Button
            icon={<EditOutlined />}
            onClick={() => {
              onEdit(appointment);
            }}
          >
            Изменить
          </Button>
          {onCreatePayment ? (
            <Button
              icon={<DollarOutlined />}
              onClick={() => {
                onCreatePayment(appointment);
              }}
            >
              Создать платеж
            </Button>
          ) : null}
          <Button
            danger
            icon={<DeleteOutlined />}
            onClick={() => {
              onDelete(appointment);
            }}
          >
            Удалить
          </Button>
        </Space>
      </Space>
    </Modal>
  );
}

function getRecurrenceSummary(key: RecurrenceType["key"], startDate?: Dayjs | null, weeklyDays?: number[]) {
  if (key === "daily") {
    return "Будет создаваться каждый день в это же время.";
  }

  if (key === "weekly") {
    const days = weeklyDayOptions
      .filter((item) => weeklyDays?.includes(item.value))
      .map((item) => item.label)
      .join(", ");

    return days ? `Будет создаваться каждую неделю: ${days}.` : "Выберите дни недели для повторения.";
  }

  if (!startDate) {
    return "Будет создаваться ежемесячно в выбранную дату.";
  }

  return `Будет создаваться ${String(startDate.date())} числа каждого месяца в это же время.`;
}

function getWeeklyBitmaskValue(date?: Dayjs | null) {
  const day = (date ?? dayjs()).day();

  if (day === 0) {
    return 64;
  }

  return 2 ** (day - 1);
}

function hasClientContacts(contacts: Appointment["client"]["contacts"]) {
  return Boolean(contacts?.phone || contacts?.telegram || contacts?.vk);
}

function formatRecurringRuleSummary(rule: NonNullable<Appointment["recurringRule"]>) {
  const ruleStart = dayjs(rule.startDate);
  const until = rule.endDate ? ` до ${dayjs(rule.endDate).format(DATE_FORMAT)}` : "";

  if (rule.key === "daily") {
    return `Каждый день с ${ruleStart.format(DATE_FORMAT)}${until}`;
  }

  if (rule.key === "weekly") {
    const weeklyDays = formatWeeklyPattern(rule.recurrencePattern);
    return `Каждую неделю: ${weeklyDays} с ${ruleStart.format(DATE_FORMAT)}${until}`;
  }

  const dayOfMonth = rule.recurrencePattern ?? ruleStart.date();
  return `Каждый месяц ${String(dayOfMonth)} числа с ${ruleStart.format(DATE_FORMAT)}${until}`;
}

function formatWeeklyPattern(pattern?: number | null) {
  if (!pattern) {
    return "дни не указаны";
  }

  return weeklyDayOptions
    .filter((item) => (pattern & item.value) === item.value)
    .map((item) => item.label)
    .join(", ");
}

function getRecurringDeleteOptions(appointment: Appointment): RecurringDeleteOption[] {
  if (isMultiDayWeeklyRecurringAppointment(appointment)) {
    const selectedDayLabel = getWeekdayLabel(dayjs(appointment.startDate));
    const weeklyPattern = formatWeeklyPattern(appointment.recurringRule?.recurrencePattern);
    const remainingDays = formatRemainingWeeklyPattern(appointment.recurringRule?.recurrencePattern, dayjs(appointment.startDate));

    return [
      {
        scope: "single",
        label: "Только эту запись",
        description: `Удалится только выбранная запись на ${selectedDayLabel}. Остальная серия останется без изменений.`,
      },
      {
        scope: "weekday-this-and-following",
        label: "Эту и следующие в этот день недели",
        description: `Удалятся записи на ${selectedDayLabel}, начиная с этой даты. Дни ${remainingDays} останутся в серии.`,
      },
      {
        scope: "weekday-all",
        label: "Все записи в этот день недели",
        description: `Удалятся все записи на ${selectedDayLabel}. Дни ${remainingDays} останутся в серии.`,
      },
      {
        scope: "this-and-following",
        label: "Эту и следующие во все дни недели",
        description: `Удалятся записи начиная с этой даты для всех дней серии: ${weeklyPattern}.`,
      },
      {
        scope: "all",
        label: "Всю серию во все дни недели",
        description: `Удалятся все записи серии для дней: ${weeklyPattern}.`,
      },
    ];
  }

  return [
    {
      scope: "single",
      label: "Только эту запись",
      description: "Удалится только выбранная запись.",
    },
    {
      scope: "this-and-following",
      label: "Эту и следующие",
      description: "Удалятся записи начиная с этой даты.",
    },
    {
      scope: "all",
      label: "Все записи",
      description: "Удалится вся серия.",
    },
  ];
}

function isMultiDayWeeklyRecurringAppointment(appointment: Appointment) {
  if (appointment.recurringRule?.key !== "weekly" || !appointment.recurringRule.recurrencePattern) {
    return false;
  }

  const recurrencePattern = appointment.recurringRule.recurrencePattern;
  const selectedDaysCount = weeklyDayOptions.filter((item) => (recurrencePattern & item.value) === item.value).length;

  return selectedDaysCount > 1;
}

function getWeekdayLabel(date: Dayjs) {
  const label = weeklyDayOptions.find((item) => item.value === getWeeklyBitmaskValue(date))?.label;
  return label ?? "выбранный день";
}

function formatRemainingWeeklyPattern(pattern: number | null | undefined, selectedDate: Dayjs) {
  const selectedDayValue = getWeeklyBitmaskValue(selectedDate);
  const recurrencePattern = pattern ?? 0;
  const remainingLabels = weeklyDayOptions
    .filter((item) => (recurrencePattern & item.value) === item.value && item.value !== selectedDayValue)
    .map((item) => item.label);

  return remainingLabels.length ? remainingLabels.join(", ") : "другие дни не выбраны";
}

const appointmentStatusOptions: {
  value: AppointmentStatus;
  label: ReactNode;
}[] = [
  {
    value: "planned",
    label: (
      <Space size={8}>
        <ClockCircleOutlined />
        {getAppointmentStatusLabel("planned")}
      </Space>
    ),
  },
  {
    value: "completed",
    label: (
      <Space size={8}>
        <CheckOutlined />
        {getAppointmentStatusLabel("completed")}
      </Space>
    ),
  },
  {
    value: "cancelled",
    label: (
      <Space size={8}>
        <CloseOutlined />
        {getAppointmentStatusLabel("cancelled")}
      </Space>
    ),
  },
  {
    value: "burned",
    label: (
      <Space size={8}>
        <FireOutlined />
        {getAppointmentStatusLabel("burned")}
      </Space>
    ),
  },
];
