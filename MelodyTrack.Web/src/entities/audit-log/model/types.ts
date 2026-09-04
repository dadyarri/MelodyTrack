import type { RequiredApiContract } from "@/shared/api";
import type { GetAuditLogsDto } from "@/shared/api/generated/models";

export type AuditLog = RequiredApiContract<
  GetAuditLogsDto,
  "id" | "createdAtUtc" | "category" | "categoryLabel" | "action" | "actionLabel" | "entityType"
>;
