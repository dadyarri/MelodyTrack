import type { CourseEnrollment } from "@/entities/course";

/** Only teacher-assigned homework awaiting review belongs in the current task list. */
export function getPortalHomework(enrollments: readonly Pick<CourseEnrollment, "id" | "courseName" | "themes">[]) {
  return enrollments.flatMap((enrollment) =>
    enrollment.themes
      .filter((theme) => theme.state === 4 && Boolean(theme.homeworkContent?.trim()))
      .map((theme) => ({ enrollmentId: enrollment.id, courseName: enrollment.courseName, theme })),
  );
}
