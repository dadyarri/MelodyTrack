import { useMutation, useQueryClient } from "@tanstack/react-query";

import { courseEnrollmentsApi, type CourseEnrollmentThemeProgressAction, courseQueryKeys } from "@/entities/course";
import type { Ulid } from "@/shared/api";

export type UpdateCourseProgressInput = {
  themeId: Ulid;
  action: CourseEnrollmentThemeProgressAction;
};

export function useUpdateCourseProgress({
  onSuccess,
  onError,
}: {
  onSuccess?: (input: UpdateCourseProgressInput) => void;
  onError?: (error: unknown) => void;
} = {}) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ themeId, action }: UpdateCourseProgressInput) => courseEnrollmentsApi.updateThemeProgress(themeId, action),
    onSuccess: async (_, input) => {
      await queryClient.invalidateQueries({ queryKey: courseQueryKeys.enrollments.all });
      onSuccess?.(input);
    },
    onError,
  });
}
