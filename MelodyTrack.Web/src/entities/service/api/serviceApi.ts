import {
  type CreateEntityResponse,
  http,
  type PaginatedParams,
  type PaginatedResponse,
  type RequiredApiContract,
  type Ulid,
} from "@/shared/api";
import type { LookupServicesResponse } from "@/shared/api/generated/models";

import type { LookupService, Service, ServiceInput } from "../model/types";

type ServicesLookupResponse = Omit<RequiredApiContract<LookupServicesResponse, "services">, "services"> & { services: LookupService[] };

export const servicesApi = {
  list(params: PaginatedParams & { name?: string }) {
    return http.get<PaginatedResponse<Service>>("/services", { params }).then((response) => response.data);
  },
  get(id: Ulid) {
    return http.get<Service>(`/services/${id}`).then((response) => response.data);
  },
  lookup() {
    return http.get<ServicesLookupResponse>("/services/options").then((response) => response.data.services);
  },
  create(input: ServiceInput & { price: number }, options?: { idempotencyKey?: string }) {
    return http
      .post<CreateEntityResponse>("/services", input, idempotencyConfig(options?.idempotencyKey))
      .then((response) => response.data);
  },
  update(id: Ulid, input: ServiceInput, options?: { expectedActivityId?: Ulid }) {
    return http
      .patch<unknown>(`/services/${id}`, {
        ...input,
        expectedActivityId: options?.expectedActivityId,
      })
      .then(() => undefined);
  },
  updatePrice(id: Ulid, price: number, options?: { expectedActivityId?: Ulid }) {
    return http
      .patch<CreateEntityResponse>(`/services/${id}/price`, {
        price,
        expectedActivityId: options?.expectedActivityId,
      })
      .then(() => undefined);
  },
  remove(id: Ulid, options?: { expectedActivityId?: Ulid }) {
    return http
      .delete<unknown>(`/services/${id}`, {
        params: options?.expectedActivityId ? { expectedActivityId: options.expectedActivityId } : undefined,
      })
      .then(() => undefined);
  },
};

function idempotencyConfig(idempotencyKey?: string) {
  return idempotencyKey ? { headers: { "Idempotency-Key": idempotencyKey } } : undefined;
}
