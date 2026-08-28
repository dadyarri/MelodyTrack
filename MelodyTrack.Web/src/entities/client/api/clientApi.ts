import { type CreateEntityResponse, http, type PaginatedResponse, type RequiredApiContract, type Ulid } from "@/shared/api";
import type {
  CreateClientPortalLinkResponse,
  GetClientsWithNegativeBalanceResponse,
  LookupClientsResponse,
} from "@/shared/api/generated/models";

import type {
  Client,
  ClientCalendarSubscription,
  ClientHistory,
  ClientWithBalance,
  CreateClientInput,
  GetClientHistoryParams,
  ListClientsParams,
  LookupClient,
  UpdateClientInput,
} from "../model/types";

type LookupResponse = Omit<RequiredApiContract<LookupClientsResponse, "clients">, "clients"> & { clients: LookupClient[] };
type DebtorsResponse = Omit<RequiredApiContract<GetClientsWithNegativeBalanceResponse, "debtors">, "debtors"> & {
  debtors: ClientWithBalance[];
};
type PortalLinkResponse = RequiredApiContract<CreateClientPortalLinkResponse, "url">;

export const clientsApi = {
  list(params: ListClientsParams) {
    return http.get<PaginatedResponse<Client>>("/clients", { params }).then((response) => response.data);
  },
  get(id: Ulid, params?: { expectedActivityId?: Ulid }) {
    return http.get<Client>(`/clients/${id}`, { params }).then((response) => response.data);
  },
  history(id: Ulid, params?: GetClientHistoryParams) {
    return http.get<ClientHistory>(`/clients/${id}/history`, { params }).then((response) => response.data);
  },
  lookup(search?: string, signal?: AbortSignal) {
    return http
      .get<LookupResponse>("/clients/options", {
        params: search ? { search } : undefined,
        signal,
      })
      .then((response) => response.data.clients);
  },
  create(input: CreateClientInput, options?: { idempotencyKey?: string }) {
    return http
      .post<CreateEntityResponse>("/clients", input, buildIdempotencyConfig(options?.idempotencyKey))
      .then((response) => response.data);
  },
  update(id: Ulid, input: UpdateClientInput, options?: { expectedActivityId?: Ulid }) {
    return http
      .patch<unknown>(`/clients/${id}`, {
        ...input,
        expectedActivityId: options?.expectedActivityId,
      })
      .then(() => undefined);
  },
  remove(id: Ulid, options?: { expectedActivityId?: Ulid }) {
    return http
      .delete<unknown>(`/clients/${id}`, {
        params: options?.expectedActivityId ? { expectedActivityId: options.expectedActivityId } : undefined,
      })
      .then(() => undefined);
  },
  setLeadClosed(id: Ulid, isClosed: boolean) {
    return http.patch<unknown>(`/clients/${id}/lead-status`, { isClosed }).then(() => undefined);
  },
  debtors() {
    return http.get<DebtorsResponse>("/client-debts").then((response) => response.data.debtors);
  },
  exportDebtors() {
    return http.get<Blob>("/exports/client-debts", { responseType: "blob" }).then((response) => response.data);
  },
  createPortalLink(id: Ulid) {
    return http.post<PortalLinkResponse>(`/clients/${id}/portal-links`, {}).then((response) => response.data);
  },
  revokePortalLink(id: Ulid) {
    return http.delete<unknown>(`/clients/${id}/portal-links`).then(() => undefined);
  },
  regenerateCalendarSubscription(id: Ulid) {
    return http.post<ClientCalendarSubscription>(`/clients/${id}/calendar-subscriptions`, {}).then((response) => response.data);
  },
  resetPortalPin(id: Ulid) {
    return http.post<unknown>(`/clients/${id}/portal-pin-resets`, {}).then(() => undefined);
  },
};

function buildIdempotencyConfig(idempotencyKey?: string) {
  return idempotencyKey
    ? {
        headers: {
          "Idempotency-Key": idempotencyKey,
        },
      }
    : undefined;
}
