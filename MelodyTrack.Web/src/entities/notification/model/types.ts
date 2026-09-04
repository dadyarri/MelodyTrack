import type { RequiredApiContract } from "@/shared/api";
import type { NotificationResponse } from "@/shared/api/generated/models";

export type AppNotification = RequiredApiContract<NotificationResponse, "id" | "type" | "title" | "summary" | "createdAtUtc">;
