export const userQueryKeys = {
  all: ["users"] as const,
  availability: (userId?: string) => ["users", "availability", userId ?? null] as const,
  availabilities: ["users", "availability", "all"] as const,
  roles: ["roles", "lookup"] as const,
};
