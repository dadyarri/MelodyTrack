import type { RequiredApiContract } from "@/shared/api";
import type {
  ChangeVacationInput,
  CreateVacationInput,
  VacationOperationResponse,
  VacationResponse,
  VacationRestorationCandidateResponse,
} from "@/shared/api/generated/models";

export type Vacation = RequiredApiContract<
  VacationResponse,
  | "id"
  | "subjectId"
  | "subjectType"
  | "startDate"
  | "endDate"
  | "version"
  | "state"
  | "timezone"
  | "requiresApproval"
  | "canManage"
  | "canEnd"
>;
export type RestorationCandidate = RequiredApiContract<
  VacationRestorationCandidateResponse,
  "id" | "startDate" | "endDate" | "serviceName" | "available" | "blockers"
>;
export type CreateVacation = RequiredApiContract<CreateVacationInput, "startDate" | "endDate">;
export type ChangeVacation = RequiredApiContract<ChangeVacationInput, "expectedVersion" | "startDate" | "endDate">;
export type VacationOperation = RequiredApiContract<VacationOperationResponse, "requiresApproval">;
