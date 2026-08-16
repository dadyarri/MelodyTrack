import type { RecordActivity, Ulid } from "@/shared/api";

export type RecurringTaskType =
  | "appointment-reminder"
  | "birthday-greeting"
  | "trial-follow-up"
  | "inactive-client-reminder"
  | "teacher-daily-schedule"
  | "debtor-reminder"
  | "custom-task";

export type RecurringTaskListStatus = "open" | "completed" | "cancelled" | "delayed";

export interface RecurringTask {
  ruleId: Ulid;
  type: RecurringTaskType;
  recipientType: "client" | "teacher" | "external";
  deduplicationKey: string;
  clientId?: Ulid | null;
  teacherId?: Ulid | null;
  appointmentId?: Ulid | null;
  title: string;
  relatedPersonDisplayName: string;
  relevantAtUtc?: string | null;
  delayedUntilUtc?: string | null;
  businessDate: string;
  phone?: string | null;
  telegram?: string | null;
  vk?: string | null;
  preparedMessage: string;
}

export interface CreateCustomTaskInput {
  clientId?: Ulid | null;
  recipientName?: string | null;
  phone?: string | null;
  telegram?: string | null;
  vk?: string | null;
  title: string;
  messageText: string;
  dueAtUtc: string;
}

export interface RecurringTaskRule {
  id: Ulid;
  name: string;
  type: RecurringTaskType;
  isEnabled: boolean;
  messageTemplate: string;
  offsetMinutes?: number | null;
  cooldownDays?: number | null;
  lastActivity?: RecordActivity | null;
}
