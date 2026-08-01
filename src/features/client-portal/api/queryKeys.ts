export const clientPortalQueryKeys = {
  all: ["portal"] as const,
  schedule: (clientId: string | null | undefined, timezone: string) => ["portal", "schedule", clientId ?? null, timezone] as const,
  enrollments: (clientId: string | null | undefined) => ["portal", "course-enrollments", clientId ?? null] as const,
};
