import type { DateOnly } from "@microsoft/kiota-abstractions";

import type {
  CreateEntityResponse as GeneratedCreateEntityResponse,
  PageMetadata as GeneratedPageMetadata,
  RecordActivityDto,
} from "./generated/models";

export type Ulid = string;

type KiotaInfrastructureKey = "additionalData" | "getFieldDeserializers" | "serialize";

export type ApiJsonContract<T> = T extends Date | DateOnly
  ? string
  : T extends readonly (infer TItem)[]
    ? ApiJsonContract<TItem>[]
    : T extends object
      ? { [TKey in keyof T as TKey extends KiotaInfrastructureKey ? never : TKey]: ApiJsonContract<T[TKey]> }
      : T;

export type RequiredApiContract<T, TKey extends keyof ApiJsonContract<T>> = Omit<ApiJsonContract<T>, TKey> & {
  [TProperty in TKey]-?: NonNullable<ApiJsonContract<T>[TProperty]>;
};

export type PageMetadata = RequiredApiContract<GeneratedPageMetadata, "page" | "pageSize" | "total" | "hasPrevPage" | "hasNextPage">;

export interface PaginatedResponse<T> {
  items: T[];
  page: PageMetadata;
}

export interface PaginatedParams {
  page?: number;
  page_size?: number;
}

export type CreateEntityResponse = RequiredApiContract<GeneratedCreateEntityResponse, "id">;

export type RecordActivity = RequiredApiContract<RecordActivityDto, "id" | "createdAtUtc" | "category" | "action">;
