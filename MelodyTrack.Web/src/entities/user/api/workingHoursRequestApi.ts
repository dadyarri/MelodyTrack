import { http, type RequiredApiContract, type Ulid } from "@/shared/api";
import type { GetWorkingHoursRequestsResponse } from "@/shared/api/generated/models";

import type {
  CreateWorkingHoursRequestInput,
  WorkingHoursRequest,
  WorkingHoursRequestDecisionInput,
} from "../model/workingHoursRequestTypes";

type WorkingHoursRequestListResponse = Omit<RequiredApiContract<GetWorkingHoursRequestsResponse, "items">, "items"> & {
  items: WorkingHoursRequest[];
};

export const workingHoursRequestQueryKeys = {
  all: ["working-hours-requests"] as const,
  mine: ["working-hours-requests", "mine"] as const,
  review: (view: "pending" | "history") => ["working-hours-requests", "review", view] as const,
};

export const workingHoursRequestsApi = {
  listMine(signal?: AbortSignal) {
    return http.get<WorkingHoursRequestListResponse>("/working-hours-requests/mine", { signal }).then((response) => response.data.items);
  },
  listReview(view: "pending" | "history", signal?: AbortSignal) {
    return http
      .get<WorkingHoursRequestListResponse>("/working-hours-requests", { params: { view }, signal })
      .then((response) => response.data.items);
  },
  create(input: CreateWorkingHoursRequestInput) {
    return http.post<WorkingHoursRequest>("/working-hours-requests", input).then((response) => response.data);
  },
  approve(id: Ulid, input: WorkingHoursRequestDecisionInput) {
    return http.post<WorkingHoursRequest>(`/working-hours-requests/${id}/approve`, input).then((response) => response.data);
  },
  decline(id: Ulid, input: WorkingHoursRequestDecisionInput) {
    return http.post<WorkingHoursRequest>(`/working-hours-requests/${id}/decline`, input).then((response) => response.data);
  },
  cancel(id: Ulid, expectedVersion: number) {
    return http.post<unknown>(`/working-hours-requests/${id}/cancel`, { expectedVersion }).then(() => undefined);
  },
};
