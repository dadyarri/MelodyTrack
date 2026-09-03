import type { RequiredApiContract } from "@/shared/api";
import type {
  CreateWorkingHoursRequest as GeneratedCreateWorkingHoursRequest,
  VacationRequestDecisionRequest as GeneratedVacationRequestDecisionRequest,
  WorkingHoursRequestDayResponse as GeneratedWorkingHoursRequestDayResponse,
  WorkingHoursRequestResponse as GeneratedWorkingHoursRequestResponse,
} from "@/shared/api/generated/models";

import type { WeekdayKey } from "./types";

export type WorkingHoursRequestStatus = "pending" | "approved" | "declined" | "cancelled";

export type WorkingHoursRequestDay = Omit<
  RequiredApiContract<GeneratedWorkingHoursRequestDayResponse, "dayOfWeek" | "isWorkingDay">,
  "dayOfWeek"
> & {
  dayOfWeek: WeekdayKey;
};

export type WorkingHoursRequest = Omit<
  RequiredApiContract<
    GeneratedWorkingHoursRequestResponse,
    | "id"
    | "requesterUserId"
    | "requesterName"
    | "subjectUserId"
    | "subjectName"
    | "subjectClassification"
    | "status"
    | "createdAtUtc"
    | "version"
    | "requestedWorkingHours"
    | "currentWorkingHours"
  >,
  "status" | "requestedWorkingHours" | "currentWorkingHours"
> & {
  status: WorkingHoursRequestStatus;
  requestedWorkingHours: WorkingHoursRequestDay[];
  currentWorkingHours: WorkingHoursRequestDay[];
};

type GeneratedCreateInput = RequiredApiContract<GeneratedCreateWorkingHoursRequest, "workingHours">;

export type CreateWorkingHoursRequestInput = Omit<GeneratedCreateInput, "workingHours"> & {
  workingHours: Array<{
    dayOfWeek: WeekdayKey;
    isWorkingDay: boolean;
    startTime?: string | null;
    endTime?: string | null;
  }>;
};

export type WorkingHoursRequestDecisionInput = RequiredApiContract<GeneratedVacationRequestDecisionRequest, "expectedVersion">;
