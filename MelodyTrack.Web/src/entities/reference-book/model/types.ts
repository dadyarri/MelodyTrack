import type { RecordActivity, Ulid } from "@/shared/api";

export interface ReferenceBookItem {
  id: Ulid;
  name: string;
  lastActivity?: RecordActivity | null;
}
