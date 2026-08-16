import { type CreateEntityResponse, http, type PaginatedParams, type Ulid } from "@/shared/api";

import type { ExpenseInput, ExpensesResponse } from "../model/types";

export interface ExpenseListParams extends PaginatedParams {
  start?: string;
  end?: string;
  search?: string;
}

export const expensesApi = {
  list(params: ExpenseListParams) {
    return http.get<ExpensesResponse>("/expenses", { params }).then((response) => response.data);
  },
  export(params: ExpenseListParams) {
    return http.get<Blob>("/exports/expenses", { params, responseType: "blob" }).then((response) => response.data);
  },
  create(input: ExpenseInput, options?: { idempotencyKey?: string }) {
    return http
      .post<CreateEntityResponse>("/expenses", input, idempotencyConfig(options?.idempotencyKey))
      .then((response) => response.data);
  },
  update(id: Ulid, input: ExpenseInput, options?: { expectedActivityId?: Ulid }) {
    return http.patch<unknown>(`/expenses/${id}`, { ...input, expectedActivityId: options?.expectedActivityId }).then(() => undefined);
  },
  remove(id: Ulid, options?: { expectedActivityId?: Ulid }) {
    return http
      .delete<unknown>(`/expenses/${id}`, {
        params: options?.expectedActivityId ? { expectedActivityId: options.expectedActivityId } : undefined,
      })
      .then(() => undefined);
  },
};

function idempotencyConfig(idempotencyKey?: string) {
  return idempotencyKey ? { headers: { "Idempotency-Key": idempotencyKey } } : undefined;
}
