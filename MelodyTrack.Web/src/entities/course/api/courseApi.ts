import { type CreateEntityResponse, http, type RequiredApiContract, type Ulid } from "@/shared/api";
import type { GetCourseEnrollmentsResponse, GetCourseResponse, GetCoursesResponse } from "@/shared/api/generated/models";

import type { Course, CourseEnrollment, CourseEnrollmentThemeProgressAction, CourseStructureInput, CourseSummary } from "../model/types";

type CoursesResponse = Omit<RequiredApiContract<GetCoursesResponse, "courses">, "courses"> & { courses: CourseSummary[] };
type CourseResponse = Omit<RequiredApiContract<GetCourseResponse, "course">, "course"> & { course: Course };
type CourseEnrollmentsResponse = Omit<RequiredApiContract<GetCourseEnrollmentsResponse, "enrollments">, "enrollments"> & {
  enrollments: CourseEnrollment[];
};

export const coursesApi = {
  list(search?: string) {
    return http.get<CoursesResponse>("/courses", { params: search ? { search } : undefined }).then((response) => response.data.courses);
  },
  get(id: Ulid) {
    return http.get<CourseResponse>(`/courses/${id}`).then((response) => response.data.course);
  },
  create(input: Omit<CourseStructureInput, "levels" | "blocks"> & Partial<Pick<CourseStructureInput, "levels" | "blocks">>) {
    return http.post<CreateEntityResponse>("/courses", input).then((response) => response.data);
  },
  update(id: Ulid, input: CourseStructureInput, options?: { expectedActivityId?: Ulid }) {
    return http.patch<unknown>(`/courses/${id}`, { ...input, expectedActivityId: options?.expectedActivityId }).then(() => undefined);
  },
  remove(id: Ulid, options?: { expectedActivityId?: Ulid }) {
    return http
      .delete<unknown>(`/courses/${id}`, {
        params: options?.expectedActivityId ? { expectedActivityId: options.expectedActivityId } : undefined,
      })
      .then(() => undefined);
  },
};

export const courseEnrollmentsApi = {
  list(params?: { clientId?: Ulid; courseId?: Ulid }) {
    return http
      .get<CourseEnrollmentsResponse>("/course-enrollments", {
        params: params && (params.clientId || params.courseId) ? params : undefined,
      })
      .then((response) => response.data.enrollments);
  },
  create(input: { clientId: Ulid; courseId: Ulid }) {
    return http.post<CreateEntityResponse>("/course-enrollments", input).then((response) => response.data);
  },
  remove(id: Ulid) {
    return http.delete<unknown>(`/course-enrollments/${id}`).then(() => undefined);
  },
  updateThemeProgress(themeId: Ulid, action: CourseEnrollmentThemeProgressAction) {
    return http.patch<unknown>(`/course-enrollment-themes/${themeId}/progress`, { action }).then(() => undefined);
  },
};
