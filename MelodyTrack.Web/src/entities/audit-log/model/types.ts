import type { Ulid } from "@/shared/api";

export interface AuditLog {
  id: Ulid;
  createdAtUtc: string;
  category: string;
  action: string;
  entityType: string;
  entityId?: string | null;
  actorEmail?: string | null;
  actorDisplayName?: string | null;
  sourceIpAddress?: string | null;
  details?: string | null;
}
