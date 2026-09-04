import type { RequiredApiContract } from "@/shared/api";
import type {
  CreateVacationRequest as GeneratedCreateVacationRequest,
  VacationPeriodResponse as GeneratedVacationPeriodResponse,
  VacationRequestDecisionRequest as GeneratedVacationRequestDecisionRequest,
  VacationRequestResponse as GeneratedVacationRequestResponse,
} from "@/shared/api/generated/models";

export type VacationRequestStatus = "pending" | "approved" | "declined" | "cancelled";

export type VacationPeriod = RequiredApiContract<GeneratedVacationPeriodResponse, "startDate" | "endDate">;

export type VacationRequest = Omit<
  RequiredApiContract<
    GeneratedVacationRequestResponse,
    | "id"
    | "requesterType"
    | "requesterId"
    | "requesterName"
    | "subjectType"
    | "subjectId"
    | "subjectName"
    | "subjectClassification"
    | "startDate"
    | "endDate"
    | "status"
    | "createdAtUtc"
    | "version"
    | "existingVacations"
    | "conflictingAppointmentCount"
  >,
  "status" | "requesterType" | "subjectType" | "existingVacations"
> & {
  status: VacationRequestStatus;
  requesterType: "staff" | "client";
  subjectType: "staff" | "client";
  existingVacations: VacationPeriod[];
};

export type CreateVacationRequestInput = RequiredApiContract<GeneratedCreateVacationRequest, "startDate" | "endDate">;
export type VacationRequestDecisionInput = RequiredApiContract<GeneratedVacationRequestDecisionRequest, "expectedVersion">;
