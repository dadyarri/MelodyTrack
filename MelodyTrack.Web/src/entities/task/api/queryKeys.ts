export const taskQueryKeys = {
  all: ["tasks"] as const,
  due: (timezone: string, status?: string | null, type?: string | null) =>
    ["tasks", "due", timezone, status ?? null, type ?? null] as const,
  rules: ["tasks", "rules"] as const,
};
