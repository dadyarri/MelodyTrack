import { type ApiJsonContract, http, type RequiredApiContract } from "@/shared/api";
import type {
  GetVacationRestorationCandidatesResponse,
  GetVacationsResponse,
  RestoreVacationAppointmentsInput,
} from "@/shared/api/generated/models";

import type { ChangeVacation, CreateVacation, RestorationCandidate, Vacation, VacationOperation } from "../model/types";

type VacationList = Omit<ApiJsonContract<GetVacationsResponse>, "items"> & { items: Vacation[] };
type CandidateList = Omit<ApiJsonContract<GetVacationRestorationCandidatesResponse>, "items"> & { items: RestorationCandidate[] };
export type VacationSubject = { subjectType: "staff" | "client"; subjectId: string };
export const vacationQueryKeys = {
  all: ["vacations"] as const,
  list: (subject?: VacationSubject) => ["vacations", "list", subject?.subjectType ?? "self", subject?.subjectId ?? "self"] as const,
  candidates: (id?: string, version?: number) => ["vacations", "candidates", id, version] as const,
};
export const vacationsApi = {
  list(subject?: VacationSubject, signal?: AbortSignal) {
    return http
      .get<VacationList>("/vacations", { params: { ...subject, includeHistory: true }, signal })
      .then((response) => response.data.items);
  },
  create(input: CreateVacation) {
    return http.post<VacationOperation>("/vacations", input).then((response) => response.data);
  },
  update(id: string, input: ChangeVacation) {
    return http.patch<VacationOperation>(`/vacations/${id}`, input).then((response) => response.data);
  },
  remove(id: string, expectedVersion: number, message?: string) {
    return http.delete<VacationOperation>(`/vacations/${id}`, { params: { expectedVersion, message } }).then((response) => response.data);
  },
  end(id: string, expectedVersion: number) {
    return http.post<VacationOperation>(`/vacations/${id}/end`, { expectedVersion }).then((response) => response.data);
  },
  candidates(id: string, signal?: AbortSignal) {
    return http.get<CandidateList>(`/vacations/${id}/restoration-candidates`, { signal }).then((response) => response.data.items);
  },
  restore(id: string, input: RequiredApiContract<RestoreVacationAppointmentsInput, "expectedVersion" | "interruptionIds">) {
    return http.post<unknown>(`/vacations/${id}/restore-appointments`, input).then(() => undefined);
  },
};
