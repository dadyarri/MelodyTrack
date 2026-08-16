import { type CreateEntityResponse, http, type Ulid } from "@/shared/api";

import type { ReferenceBookItem } from "../model/types";

function createReferenceBookApi(collection: string, responseKey: "sources" | "categories") {
  return {
    list() {
      return http.get<Record<string, ReferenceBookItem[]>>(collection).then((response) => response.data[responseKey] ?? []);
    },
    create(input: { name: string }, options?: { idempotencyKey?: string }) {
      return http
        .post<CreateEntityResponse>(
          collection,
          input,
          options?.idempotencyKey ? { headers: { "Idempotency-Key": options.idempotencyKey } } : undefined,
        )
        .then((response) => response.data);
    },
    remove(id: Ulid, options?: { expectedActivityId?: Ulid }) {
      return http
        .delete<unknown>(`${collection}/${id}`, {
          params: options?.expectedActivityId ? { expectedActivityId: options.expectedActivityId } : undefined,
        })
        .then(() => undefined);
    },
  };
}

export const clientSourcesApi = createReferenceBookApi("/client-sources", "sources");
export const expenseCategoriesApi = createReferenceBookApi("/expense-categories", "categories");
