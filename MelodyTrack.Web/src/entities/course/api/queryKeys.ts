export const courseQueryKeys = {
  all: ["courses"] as const,
  list: (search: string) => ["courses", "list", search] as const,
  selected: (courseId?: string) => ["courses", "selected", courseId ?? null] as const,
  enrollments: {
    all: ["course-enrollments"] as const,
    list: (params?: { clientId?: string | null; courseId?: string | null }) =>
      ["course-enrollments", "list", params?.clientId ?? null, params?.courseId ?? null] as const,
  },
};
