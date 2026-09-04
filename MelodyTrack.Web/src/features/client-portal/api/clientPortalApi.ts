import type { AppointmentStatus } from "@/entities/appointment";
import type { CourseEnrollment } from "@/entities/course";
import { type ApiJsonContract, http, type RequiredApiContract } from "@/shared/api";
import type {
  ClientPortalAppointmentDto,
  ClientPortalCourseThemeDto,
  GetClientPortalScheduleResponse,
  GetCourseEnrollmentsResponse,
} from "@/shared/api/generated/models";

type ClientPortalCourseTheme = RequiredApiContract<ClientPortalCourseThemeDto, "id" | "title">;
export type ClientPortalAppointment = Omit<
  RequiredApiContract<ClientPortalAppointmentDto, "id" | "startDate" | "endDate" | "status" | "isTrial">,
  "status" | "courseTheme"
> & {
  status: AppointmentStatus;
  courseTheme?: ClientPortalCourseTheme | null;
};
type ClientPortalScheduleResponse = Omit<ApiJsonContract<GetClientPortalScheduleResponse>, "nextAppointment"> & {
  nextAppointment?: ClientPortalAppointment | null;
};
type ClientPortalCourseEnrollmentsResponse = Omit<RequiredApiContract<GetCourseEnrollmentsResponse, "enrollments">, "enrollments"> & {
  enrollments: CourseEnrollment[];
};

export const clientPortalApi = {
  schedule(params: { timezone: string }) {
    return http
      .get<ClientPortalScheduleResponse>("/client-portal/schedule", { params })
      .then((response) => response.data.nextAppointment ?? null);
  },
  courseEnrollments() {
    return http
      .get<ClientPortalCourseEnrollmentsResponse>("/client-portal/course-enrollments")
      .then((response) => response.data.enrollments);
  },
};
