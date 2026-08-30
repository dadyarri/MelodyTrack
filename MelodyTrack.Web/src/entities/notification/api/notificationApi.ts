import { http, type RequiredApiContract, type Ulid } from "@/shared/api";
import type {
  GetNotificationsResponse,
  PushSubscriptionRequest,
  RevokePushSubscriptionRequest,
  WebPushConfigurationResponse,
} from "@/shared/api/generated/models";

import type { AppNotification } from "../model/types";

type NotificationListResponse = Omit<RequiredApiContract<GetNotificationsResponse, "items" | "unreadCount">, "items"> & {
  items: AppNotification[];
};
type PushConfiguration = RequiredApiContract<WebPushConfigurationResponse, "enabled">;
type SubscribeRequest = RequiredApiContract<PushSubscriptionRequest, "endpoint" | "p256Dh" | "auth">;
type RevokeRequest = RequiredApiContract<RevokePushSubscriptionRequest, "endpoint">;

export const notificationApi = {
  list(signal?: AbortSignal) {
    return http.get<NotificationListResponse>("/notifications", { signal }).then((response) => response.data);
  },
  markRead(id: Ulid) {
    return http.post<unknown>(`/notifications/${id}/read`, {}).then(() => undefined);
  },
  markAllRead() {
    return http.post<unknown>("/notifications/read-all", {}).then(() => undefined);
  },
  getPushConfiguration(signal?: AbortSignal) {
    return http.get<PushConfiguration>("/notifications/push/configuration", { signal }).then((response) => response.data);
  },
  subscribe(request: SubscribeRequest) {
    return http.post<unknown>("/notifications/push/subscription", request).then(() => undefined);
  },
  revokePushSubscription(request: RevokeRequest) {
    return http.post<unknown>("/notifications/push/subscription/revoke", request).then(() => undefined);
  },
};
