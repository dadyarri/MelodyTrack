export const userQueryKeys = {
  all: ["users"] as const,
  availability: (userId?: string) => ["users", "availability", userId ?? null] as const,
  availabilities: ["users", "availability", "all"] as const,
  vacationAppointmentConflictCount: (userId?: string, startDate?: string, endDate?: string) =>
    ["users", "vacation-appointment-conflict-count", userId ?? null, startDate ?? null, endDate ?? null] as const,
  roles: ["roles", "lookup"] as const,
};
