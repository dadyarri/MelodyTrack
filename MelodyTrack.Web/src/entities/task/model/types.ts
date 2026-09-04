import type { RecordActivity, RequiredApiContract } from "@/shared/api";
import type { CreateCustomTaskRequest, RecurringTaskDto, RecurringTaskRuleDto } from "@/shared/api/generated/models";

export type RecurringTaskType =
  | "appointment-reminder"
  | "birthday-greeting"
  | "trial-follow-up"
  | "inactive-client-reminder"
  | "teacher-daily-schedule"
  | "debtor-reminder"
  | "custom-task";

export type RecurringTaskListStatus = "open" | "completed" | "cancelled" | "delayed";

export type RecurringTask = Omit<
  RequiredApiContract<
    RecurringTaskDto,
    "ruleId" | "type" | "recipientType" | "deduplicationKey" | "title" | "relatedPersonDisplayName" | "businessDate" | "preparedMessage"
  >,
  "type" | "recipientType"
> & {
  type: RecurringTaskType;
  recipientType: "client" | "teacher" | "external";
};

export type CreateCustomTaskInput = RequiredApiContract<CreateCustomTaskRequest, "title" | "messageText" | "dueAtUtc">;

export type RecurringTaskRule = Omit<
  RequiredApiContract<RecurringTaskRuleDto, "id" | "name" | "type" | "isEnabled" | "messageTemplate">,
  "type" | "lastActivity"
> & {
  type: RecurringTaskType;
  lastActivity?: RecordActivity | null;
};
