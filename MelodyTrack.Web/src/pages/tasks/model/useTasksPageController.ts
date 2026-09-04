import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { App as AntdApp, Form } from "antd";
import dayjs, { type Dayjs } from "dayjs";
import { useMemo, useState } from "react";
import * as v from "valibot";

import { getSocialHandle } from "@/entities/client";
import { useAuth } from "@/entities/session";
import {
  type RecurringTask,
  type RecurringTaskListStatus,
  type RecurringTaskRule,
  type RecurringTaskType,
  taskQueryKeys,
  tasksApi,
} from "@/entities/task";
import type { Ulid } from "@/shared/api";
import { getApiErrorMessages } from "@/shared/api";
import { downloadBlob } from "@/shared/lib";
import { getBackgroundRefetchInterval } from "@/shared/lib";
import { findItemInQueryData, handleStaleEntityConflict, isActivityStale } from "@/shared/lib";
import { jsonDurableFormCodec, useDurableForm, useUrlState } from "@/shared/lib/react";
import { useCopyTextModal } from "@/shared/ui";

export type RecurringTaskRuleFormValues = {
  isEnabled: boolean;
  messageTemplate: string;
  offsetMinutes?: number | null;
  cooldownDays?: number | null;
};

type DelayTaskFormValues = {
  delayUntil: Dayjs;
};

export type CustomTaskFormValues = {
  recipientMode: "client" | "external";
  clientId?: string;
  recipientName?: string;
  phone?: string;
  telegram?: string;
  vk?: string;
  title: string;
  messageText: string;
  dueAt: Dayjs;
};

export type PreparedScheduleShare = {
  blob: Blob;
  fileName: string;
  messengerUrl: string;
};

type CustomTaskDraftValues = Partial<Omit<CustomTaskFormValues, "dueAt">> & { dueAt?: string };
const taskRuleDraftSchema = v.object({
  isEnabled: v.boolean(),
  messageTemplate: v.string(),
  offsetMinutes: v.optional(v.nullable(v.number())),
  cooldownDays: v.optional(v.nullable(v.number())),
});
const customTaskDraftSchema = v.object({
  recipientMode: v.picklist(["client", "external"]),
  clientId: v.optional(v.string()),
  recipientName: v.optional(v.string()),
  phone: v.optional(v.string()),
  telegram: v.optional(v.string()),
  vk: v.optional(v.string()),
  title: v.optional(v.string()),
  messageText: v.optional(v.string()),
  dueAt: v.optional(v.string()),
});
const taskRuleDraftCodec = jsonDurableFormCodec<RecurringTaskRuleFormValues>();
export const customTaskDraftCodec = {
  serialize: (values: Partial<CustomTaskFormValues>): CustomTaskDraftValues => ({ ...values, dueAt: values.dueAt?.toISOString() }),
  deserialize: (values: CustomTaskDraftValues): Partial<CustomTaskFormValues> => ({
    ...values,
    dueAt: values.dueAt ? dayjs(values.dueAt) : undefined,
  }),
};

export function useTasksPageController() {
  const auth = useAuth();
  const copyTextModal = useCopyTextModal(auth.user?.id);
  const { searchParams, setUrlState } = useUrlState();
  const taskAutoRefreshMs = 30_000;
  const timezone = useMemo(() => Intl.DateTimeFormat().resolvedOptions().timeZone, []);
  const status = readTaskStatus(searchParams.get("status"));
  const type = readTaskType(searchParams.get("type"));
  const activeTab = searchParams.get("tab") === "rules" ? "rules" : "tasks";
  const setStatus = (nextStatus: RecurringTaskListStatus) => {
    setUrlState({ status: nextStatus === "open" ? null : nextStatus });
  };
  const setType = (nextType: RecurringTaskType | "all") => {
    setUrlState({ type: nextType === "all" ? null : nextType });
  };
  const setActiveTab = (nextTab: "tasks" | "rules") => {
    setUrlState({ tab: nextTab === "tasks" ? null : nextTab });
  };
  const [editingRule, setEditingRule] = useState<RecurringTaskRule | null>(null);
  const [editingRuleBaselineActivityId, setEditingRuleBaselineActivityId] = useState<Ulid | null | undefined>();
  const [ruleForm] = Form.useForm<RecurringTaskRuleFormValues>();
  const [customTaskForm] = Form.useForm<CustomTaskFormValues>();
  const [delayTaskForm] = Form.useForm<DelayTaskFormValues>();
  const [delayingTask, setDelayingTask] = useState<RecurringTask | null>(null);
  const [preparedScheduleShare, setPreparedScheduleShare] = useState<PreparedScheduleShare | null>(null);
  const [isCustomTaskModalOpen, setIsCustomTaskModalOpen] = useState(false);
  const ruleDraft = useDurableForm({
    key: editingRule ? `draft:tasks:rules:edit:${editingRule.id}` : null,
    schema: taskRuleDraftSchema,
    form: ruleForm,
    codec: taskRuleDraftCodec,
    enabled: editingRule !== null,
    entity: editingRule ? { id: editingRule.id, baselineVersion: editingRuleBaselineActivityId ?? null } : undefined,
  });
  const customTaskDraft = useDurableForm({
    key: "draft:tasks:custom:create",
    schema: customTaskDraftSchema,
    form: customTaskForm,
    codec: customTaskDraftCodec,
    enabled: isCustomTaskModalOpen,
  });
  const queryClient = useQueryClient();
  const { message, modal } = AntdApp.useApp();

  const query = useQuery({
    queryKey: taskQueryKeys.due(timezone, status, type === "all" ? null : type),
    queryFn: () => tasksApi.due({ timezone, status, type }),
    refetchInterval: getBackgroundRefetchInterval(
      activeTab !== "tasks" || Boolean(editingRule || delayingTask) || isCustomTaskModalOpen,
      taskAutoRefreshMs,
    ),
  });
  const rulesQuery = useQuery({
    queryKey: taskQueryKeys.rules,
    queryFn: () => tasksApi.rules(),
  });
  const currentEditingRule = editingRule ? (rulesQuery.data?.find((rule) => rule.id === editingRule.id) ?? editingRule) : null;
  const isEditingRuleStale = currentEditingRule
    ? isActivityStale(currentEditingRule.lastActivity?.id, editingRuleBaselineActivityId)
    : false;

  const completeMutation = useMutation({
    mutationFn: (task: RecurringTask) =>
      tasksApi.complete({
        timezone,
        ruleId: task.ruleId,
        type: task.type,
        deduplicationKey: task.deduplicationKey,
        clientId: task.clientId,
        teacherId: task.teacherId,
        appointmentId: task.appointmentId,
        preparedMessage: task.preparedMessage,
      }),
    onSuccess: async () => {
      void message.success("Задача отмечена выполненной");
      await queryClient.invalidateQueries({ queryKey: taskQueryKeys.all });
    },
  });

  const cancelMutation = useMutation({
    mutationFn: (task: RecurringTask) =>
      tasksApi.cancel({
        timezone,
        ruleId: task.ruleId,
        type: task.type,
        deduplicationKey: task.deduplicationKey,
        clientId: task.clientId,
        teacherId: task.teacherId,
        appointmentId: task.appointmentId,
      }),
    onSuccess: async () => {
      void message.success("Задача отменена");
      await queryClient.invalidateQueries({ queryKey: taskQueryKeys.all });
    },
  });

  const delayMutation = useMutation({
    mutationFn: ({ task, delayUntil }: { task: RecurringTask; delayUntil: Dayjs }) =>
      tasksApi.delay({
        timezone,
        ruleId: task.ruleId,
        type: task.type,
        deduplicationKey: task.deduplicationKey,
        delayUntilUtc: delayUntil.toISOString(),
        clientId: task.clientId,
        teacherId: task.teacherId,
        appointmentId: task.appointmentId,
      }),
    onSuccess: async () => {
      void message.success("Задача отложена");
      setDelayingTask(null);
      delayTaskForm.resetFields();
      await queryClient.invalidateQueries({ queryKey: taskQueryKeys.all });
    },
  });
  const updateRuleMutation = useMutation({
    mutationFn: ({ id, values, expectedActivityId }: { id: Ulid; values: RecurringTaskRuleFormValues; expectedActivityId?: Ulid }) =>
      tasksApi.updateRule(
        id,
        {
          isEnabled: values.isEnabled,
          messageTemplate: values.messageTemplate.trim(),
          offsetMinutes: values.offsetMinutes ?? null,
          cooldownDays: values.cooldownDays ?? null,
        },
        { expectedActivityId },
      ),
    onSuccess: async () => {
      void message.success("Правило обновлено");
      await ruleDraft.clearAfterSuccess();
      setEditingRule(null);
      setEditingRuleBaselineActivityId(undefined);
      ruleForm.resetFields();
      await queryClient.invalidateQueries({ queryKey: taskQueryKeys.rules });
    },
    meta: { suppressErrorNotification: true },
    onError: async (error, variables) => {
      await handleStaleEntityConflict({
        error,
        modal,
        queryClient,
        invalidateQueryKey: taskQueryKeys.rules,
        showErrors: (currentError) => {
          for (const errorMessage of getApiErrorMessages(currentError)) {
            void message.error(errorMessage);
          }
        },
        title: "Правило уже изменено",
        okText: "Сохранить все равно",
        cancelText: "Обновить данные",
        onConfirm: (conflict) => {
          updateRuleMutation.mutate({
            id: variables.id,
            values: variables.values,
            expectedActivityId: conflict.currentActivity?.id,
          });
        },
        onReload: () => {
          const freshRule =
            findItemInQueryData(queryClient, taskQueryKeys.rules, (data) => data as RecurringTaskRule[] | undefined, variables.id) ??
            currentEditingRule;
          if (!freshRule) {
            return;
          }

          setEditingRule(freshRule);
          setEditingRuleBaselineActivityId(freshRule.lastActivity?.id ?? null);
          ruleForm.setFieldsValue({
            isEnabled: freshRule.isEnabled,
            messageTemplate: freshRule.messageTemplate,
            offsetMinutes: freshRule.offsetMinutes ?? undefined,
            cooldownDays: freshRule.cooldownDays ?? undefined,
          });
        },
      });
    },
  });
  const createCustomTaskMutation = useMutation({
    mutationFn: (values: CustomTaskFormValues) =>
      tasksApi.createCustom({
        clientId: values.recipientMode === "client" ? (values.clientId ?? null) : null,
        recipientName: values.recipientMode === "external" ? (values.recipientName?.trim() ?? null) : null,
        phone: values.recipientMode === "external" ? (values.phone?.trim() ?? null) : null,
        telegram: values.recipientMode === "external" ? (values.telegram?.trim() ?? null) : null,
        vk: values.recipientMode === "external" ? (values.vk?.trim() ?? null) : null,
        title: values.title.trim(),
        messageText: values.messageText.trim(),
        dueAtUtc: values.dueAt.toISOString(),
      }),
    onSuccess: async () => {
      void message.success("Пользовательская задача создана");
      await customTaskDraft.clearAfterSuccess();
      setIsCustomTaskModalOpen(false);
      customTaskForm.resetFields();
      await queryClient.invalidateQueries({ queryKey: taskQueryKeys.all });
    },
  });

  const getFreshTaskForAction = async (task: RecurringTask) => {
    try {
      const refreshedTasks = await queryClient.fetchQuery({
        queryKey: taskQueryKeys.due(timezone, status, type === "all" ? null : type),
        queryFn: () => tasksApi.due({ timezone, status, type }),
      });
      const freshTask =
        refreshedTasks.find((item) => item.deduplicationKey === task.deduplicationKey) ??
        refreshedTasks.find(
          (item) =>
            item.ruleId === task.ruleId &&
            item.type === task.type &&
            item.clientId === task.clientId &&
            item.teacherId === task.teacherId &&
            item.appointmentId === task.appointmentId,
        ) ??
        null;

      if (!freshTask) {
        void message.error("Задача больше не актуальна.");
      }

      return freshTask;
    } catch (error) {
      for (const errorMessage of getApiErrorMessages(error)) {
        void message.error(errorMessage);
      }
      return null;
    }
  };

  return {
    activeTab,
    setActiveTab,
    status,
    setStatus,
    type,
    setType,
    query,
    tasks: query.data ?? [],
    rulesQuery,
    rules: rulesQuery.data ?? [],
    ruleForm,
    customTaskForm,
    ruleDraft,
    customTaskDraft,
    editingRule,
    currentEditingRule,
    isEditingRuleStale: isEditingRuleStale || ruleDraft.isStale,
    updateRuleMutation,
    createCustomTaskMutation,
    isCustomTaskModalOpen,
    openCustomTaskModal: () => {
      setIsCustomTaskModalOpen(true);
      if (!customTaskDraft.hasDraft)
        customTaskForm.setFieldsValue({
          recipientMode: "client",
          clientId: undefined,
          recipientName: undefined,
          phone: undefined,
          telegram: undefined,
          vk: undefined,
          title: "",
          messageText: "",
          dueAt: undefined,
        });
    },
    closeCustomTaskModal: () => {
      setIsCustomTaskModalOpen(false);
    },
    submitCustomTask: async (values: CustomTaskFormValues) => {
      await createCustomTaskMutation.mutateAsync(values);
    },
    onRuleValuesChange: ruleDraft.formProps.onValuesChange,
    onCustomTaskValuesChange: customTaskDraft.formProps.onValuesChange,
    completeTask: (task: RecurringTask) => completeMutation.mutateAsync(task),
    cancelTask: (task: RecurringTask) => cancelMutation.mutateAsync(task),
    openDelayTask: (task: RecurringTask) => {
      setDelayingTask(task);
      delayTaskForm.setFieldsValue({
        delayUntil: undefined,
      });
    },
    closeDelayTask: () => {
      setDelayingTask(null);
      delayTaskForm.resetFields();
    },
    submitDelayTask: async (values: DelayTaskFormValues) => {
      if (!delayingTask) {
        return;
      }

      await delayMutation.mutateAsync({ task: delayingTask, delayUntil: values.delayUntil });
    },
    completeMutation,
    cancelMutation,
    delayMutation,
    delayTaskForm,
    delayingTask,
    copyTextModalProps: copyTextModal.copyTextModalProps,
    preparedScheduleShare,
    closePreparedScheduleShare: () => {
      setPreparedScheduleShare(null);
    },
    copyTaskPreparedMessage: async (task: RecurringTask) => {
      const freshTask = await getFreshTaskForAction(task);
      if (!freshTask) {
        return;
      }

      copyTextModal.openCopyTextModal({
        value: freshTask.preparedMessage,
        title: "Сообщение готово",
        description: "Текст обновлён по актуальным данным задачи. Проверьте и скопируйте его.",
        fieldLabel: "Текст сообщения",
        copyButtonLabel: "Скопировать текст",
        copiedConfirmation: "Текст сообщения скопирован",
      });
    },
    openTaskTelegram: async (task: RecurringTask) => {
      if (!task.telegram) {
        return;
      }

      const freshTask = await getFreshTaskForAction(task);
      if (!freshTask) {
        return;
      }

      if (!freshTask.telegram) {
        void message.error("У задачи больше не указан Telegram.");
        return;
      }

      const telegramLink = buildTelegramLink(freshTask.telegram, freshTask.preparedMessage);
      if (!telegramLink) {
        void message.error("Не удалось сформировать ссылку Telegram.");
        return;
      }

      window.location.href = telegramLink;
    },
    openTaskVk: async (task: RecurringTask) => {
      if (!task.vk) {
        return;
      }

      const freshTask = await getFreshTaskForAction(task);
      if (!freshTask) {
        return;
      }

      if (!freshTask.vk) {
        void message.error("У задачи больше не указан VK.");
        return;
      }

      const vkLink = buildVkLink(freshTask.vk);
      if (!vkLink) {
        void message.error("Не удалось сформировать ссылку VK.");
        return;
      }

      copyTextModal.openCopyTextModal({
        value: freshTask.preparedMessage,
        title: "Сообщение для VK готово",
        description: "Скопируйте обновлённый текст, затем откройте диалог VK.",
        fieldLabel: "Текст сообщения",
        copyButtonLabel: "Скопировать текст",
        copiedConfirmation: "Текст скопирован. Теперь можно открыть VK",
        followUpAction: {
          label: "Открыть VK",
          href: vkLink,
          target: "_blank",
        },
      });
    },
    downloadTeacherSchedule: async (task: RecurringTask) => {
      if (!task.teacherId) {
        void message.error("Не указан преподаватель для расписания.");
        return;
      }

      try {
        const blob = await tasksApi.teacherScheduleImage({
          teacherId: task.teacherId,
          date: task.businessDate,
          timezone,
        });

        downloadBlob(blob, `teacher_schedule_${task.businessDate}_${task.teacherId}.png`);
      } catch (error) {
        for (const errorMessage of getApiErrorMessages(error)) {
          void message.error(errorMessage);
        }
      }
    },
    openTeacherScheduleMessenger: async (task: RecurringTask, messengerUrl: string) => {
      if (!task.teacherId) {
        void message.error("Не указан преподаватель для расписания.");
        return;
      }

      try {
        const blob = await tasksApi.teacherScheduleImage({
          teacherId: task.teacherId,
          date: task.businessDate,
          timezone,
        });

        setPreparedScheduleShare({
          blob: blob.type === "image/png" ? blob : new Blob([blob], { type: "image/png" }),
          fileName: `teacher_schedule_${task.businessDate}_${task.teacherId}.png`,
          messengerUrl,
        });
      } catch (error) {
        for (const errorMessage of getApiErrorMessages(error)) {
          void message.error(errorMessage);
        }
      }
    },
    openRuleEditor: (rule: RecurringTaskRule) => {
      setEditingRule(rule);
      setEditingRuleBaselineActivityId(rule.lastActivity?.id ?? null);
      ruleForm.setFieldsValue({
        isEnabled: rule.isEnabled,
        messageTemplate: rule.messageTemplate,
        offsetMinutes: rule.offsetMinutes ?? undefined,
        cooldownDays: rule.cooldownDays ?? undefined,
      });
    },
    closeRuleEditor: () => {
      setEditingRule(null);
      setEditingRuleBaselineActivityId(undefined);
    },
    submitRuleEditor: (values: RecurringTaskRuleFormValues) => {
      if (!editingRule) {
        return;
      }

      updateRuleMutation.mutate({
        id: editingRule.id,
        values,
        expectedActivityId: editingRuleBaselineActivityId ?? undefined,
      });
    },
  };
}

const taskStatuses = new Set<RecurringTaskListStatus>(["open", "completed", "cancelled", "delayed"]);
const taskTypes = new Set<RecurringTaskType>([
  "appointment-reminder",
  "birthday-greeting",
  "trial-follow-up",
  "inactive-client-reminder",
  "teacher-daily-schedule",
  "debtor-reminder",
  "custom-task",
]);

function readTaskStatus(value: string | null): RecurringTaskListStatus {
  return value && taskStatuses.has(value as RecurringTaskListStatus) ? (value as RecurringTaskListStatus) : "open";
}

function readTaskType(value: string | null): RecurringTaskType | "all" {
  return value && taskTypes.has(value as RecurringTaskType) ? (value as RecurringTaskType) : "all";
}

function buildTelegramLink(value: string, message: string) {
  const handle = getSocialHandle(value, "telegram");
  return handle ? `tg://resolve?domain=${encodeURIComponent(handle)}&text=${encodeURIComponent(message)}` : undefined;
}

function buildVkLink(value: string) {
  const handle = getSocialHandle(value, "vk");
  return handle ? `https://vk.me/${handle}` : undefined;
}
