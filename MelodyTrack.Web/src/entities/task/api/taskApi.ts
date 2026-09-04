import { type CreateEntityResponse, http, type RequiredApiContract, type Ulid } from "@/shared/api";
import type { GetDueRecurringTasksResponse, GetRecurringTaskRulesResponse } from "@/shared/api/generated/models";

import type { CreateCustomTaskInput, RecurringTask, RecurringTaskListStatus, RecurringTaskRule, RecurringTaskType } from "../model/types";

type DueTasksResponse = Omit<RequiredApiContract<GetDueRecurringTasksResponse, "tasks">, "tasks"> & { tasks: RecurringTask[] };
type RulesResponse = Omit<RequiredApiContract<GetRecurringTaskRulesResponse, "rules">, "rules"> & { rules: RecurringTaskRule[] };

interface TaskIdentity {
  timezone: string;
  ruleId: Ulid;
  type: RecurringTaskType;
  deduplicationKey: string;
  clientId?: Ulid | null;
  teacherId?: Ulid | null;
  appointmentId?: Ulid | null;
}

export const tasksApi = {
  due(params: { timezone: string; status?: RecurringTaskListStatus; type?: RecurringTaskType | "all" }) {
    return http
      .get<DueTasksResponse>("/tasks", {
        params: { ...params, type: params.type && params.type !== "all" ? params.type : undefined },
      })
      .then((response) => response.data.tasks);
  },
  complete(input: TaskIdentity & { preparedMessage?: string | null }) {
    const { deduplicationKey, ...payload } = input;
    return http.post<unknown>(`/tasks/${encodeURIComponent(deduplicationKey)}/completion`, payload).then(() => undefined);
  },
  cancel(input: TaskIdentity) {
    const { deduplicationKey, ...payload } = input;
    return http.post<unknown>(`/tasks/${encodeURIComponent(deduplicationKey)}/cancellation`, payload).then(() => undefined);
  },
  delay(input: TaskIdentity & { delayUntilUtc: string }) {
    const { deduplicationKey, ...payload } = input;
    return http.post<unknown>(`/tasks/${encodeURIComponent(deduplicationKey)}/deferral`, payload).then(() => undefined);
  },
  teacherScheduleImage(params: { teacherId: Ulid; date: string; timezone: string }) {
    return http.get<Blob>("/exports/teacher-schedule", { params, responseType: "blob" }).then((response) => response.data);
  },
  rules() {
    return http.get<RulesResponse>("/recurring-task-rules").then((response) => response.data.rules);
  },
  updateRule(
    id: Ulid,
    input: { isEnabled: boolean; messageTemplate: string; offsetMinutes?: number | null; cooldownDays?: number | null },
    options?: { expectedActivityId?: Ulid },
  ) {
    return http
      .patch<unknown>(`/recurring-task-rules/${id}`, { ...input, expectedActivityId: options?.expectedActivityId })
      .then(() => undefined);
  },
  createCustom(input: CreateCustomTaskInput) {
    return http.post<CreateEntityResponse>("/tasks", input).then((response) => response.data);
  },
};
