export const notificationQueryKeys = {
  all: ["notifications"] as const,
  list: () => [...notificationQueryKeys.all, "list"] as const,
  pushConfiguration: () => [...notificationQueryKeys.all, "push-configuration"] as const,
};
