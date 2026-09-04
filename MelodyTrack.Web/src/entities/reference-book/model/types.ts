import type { RecordActivity, RequiredApiContract } from "@/shared/api";
import type { ReferenceBookItemDto } from "@/shared/api/generated/models";

export type ReferenceBookItem = Omit<RequiredApiContract<ReferenceBookItemDto, "id" | "name">, "lastActivity"> & {
  lastActivity?: RecordActivity | null;
};
