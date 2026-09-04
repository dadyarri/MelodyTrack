import { http, type RequiredApiContract, type Ulid } from "@/shared/api";
import type { GetVacationRequestsResponse } from "@/shared/api/generated/models";

import type { CreateVacationRequestInput, VacationRequest, VacationRequestDecisionInput } from "../model/types";

type VacationRequestListResponse = Omit<RequiredApiContract<GetVacationRequestsResponse, "items">, "items"> & {
  items: VacationRequest[];
};

export const vacationRequestQueryKeys = {
  all: ["vacation-requests"] as const,
  mine: (portal: boolean) => ["vacation-requests", portal ? "portal-mine" : "staff-mine"] as const,
  review: (view: "pending" | "history") => ["vacation-requests", "review", view] as const,
};

export const vacationRequestsApi = {
  listMine(portal: boolean, signal?: AbortSignal) {
    const path = portal ? "/client-portal/vacation-requests" : "/vacation-requests/mine";
    return http.get<VacationRequestListResponse>(path, { signal }).then((response) => response.data.items);
  },
  listReview(view: "pending" | "history", signal?: AbortSignal) {
    return http
      .get<VacationRequestListResponse>("/vacation-requests", { params: { view }, signal })
      .then((response) => response.data.items);
  },
  create(input: CreateVacationRequestInput, portal: boolean) {
    const path = portal ? "/client-portal/vacation-requests" : "/vacation-requests";
    return http.post<VacationRequest>(path, input).then((response) => response.data);
  },
  approve(id: Ulid, input: VacationRequestDecisionInput) {
    return http.post<VacationRequest>(`/vacation-requests/${id}/approve`, input).then((response) => response.data);
  },
  decline(id: Ulid, input: VacationRequestDecisionInput) {
    return http.post<VacationRequest>(`/vacation-requests/${id}/decline`, input).then((response) => response.data);
  },
  cancel(id: Ulid, expectedVersion: number) {
    return http.post<unknown>(`/vacation-requests/${id}/cancel`, { expectedVersion }).then(() => undefined);
  },
};
