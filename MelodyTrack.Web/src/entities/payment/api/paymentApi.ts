import { type CreateEntityResponse, http, type PaginatedParams, type Ulid } from "@/shared/api";

import type { PaymentInput, PaymentsResponse } from "../model/types";

export interface PaymentListParams extends PaginatedParams {
  firstName?: string;
  lastName?: string;
  search?: string;
  clientId?: string;
  serviceId?: string;
  start?: string;
  end?: string;
}

export const paymentsApi = {
  list(params: PaymentListParams) {
    return http.get<PaymentsResponse>("/payments", { params }).then((response) => response.data);
  },
  export(params: PaymentListParams) {
    return http.get<Blob>("/exports/payments", { params, responseType: "blob" }).then((response) => response.data);
  },
  create(input: PaymentInput, options?: { idempotencyKey?: string }) {
    return http
      .post<CreateEntityResponse>("/payments", input, idempotencyConfig(options?.idempotencyKey))
      .then((response) => response.data);
  },
  update(id: Ulid, input: PaymentInput, options?: { expectedActivityId?: Ulid }) {
    return http
      .patch<unknown>(`/payments/${id}`, {
        ...input,
        expectedActivityId: options?.expectedActivityId,
      })
      .then(() => undefined);
  },
  remove(id: Ulid, options?: { expectedActivityId?: Ulid }) {
    return http
      .delete<unknown>(`/payments/${id}`, {
        params: options?.expectedActivityId ? { expectedActivityId: options.expectedActivityId } : undefined,
      })
      .then(() => undefined);
  },
};

function idempotencyConfig(idempotencyKey?: string) {
  return idempotencyKey ? { headers: { "Idempotency-Key": idempotencyKey } } : undefined;
}
