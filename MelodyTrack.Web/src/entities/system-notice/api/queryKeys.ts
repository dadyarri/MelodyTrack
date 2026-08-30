export const systemNoticeQueryKeys = {
  all: ["system-notices"] as const,
  authenticated: () => [...systemNoticeQueryKeys.all, "authenticated"] as const,
  preAuth: () => [...systemNoticeQueryKeys.all, "pre-auth"] as const,
};
