import type { RequiredApiContract } from "@/shared/api";
import type { SystemNoticeResponse } from "@/shared/api/generated/models";

export type SystemNotice = RequiredApiContract<
  SystemNoticeResponse,
  "id" | "title" | "body" | "severity" | "createdAtUtc" | "dismissible" | "audienceType" | "showBeforeAuthentication"
>;
