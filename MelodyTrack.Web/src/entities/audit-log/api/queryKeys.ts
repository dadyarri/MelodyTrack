export const auditLogQueryKeys = {
  all: ["audit"] as const,
  list: (page: number, search: string, timezone: string) => ["audit", "list", page, search, timezone] as const,
};
