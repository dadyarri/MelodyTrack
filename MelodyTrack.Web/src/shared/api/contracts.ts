export type Ulid = string;

export interface PageMetadata {
  page: number;
  pageSize: number;
  total: number;
  hasPrevPage: boolean;
  hasNextPage: boolean;
}

export interface PaginatedResponse<T> {
  items: T[];
  page: PageMetadata;
}

export interface PaginatedParams {
  page?: number;
  page_size?: number;
}

export interface CreateEntityResponse {
  id: Ulid;
}

export interface RecordActivity {
  id: Ulid;
  createdAtUtc: string;
  category: string;
  action: string;
  actorEmail?: string | null;
  actorDisplayName?: string | null;
  sourceIpAddress?: string | null;
  details?: string | null;
}
