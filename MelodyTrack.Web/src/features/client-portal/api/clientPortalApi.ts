import type { AppointmentStatus } from "@/entities/appointment";
import type { CourseEnrollment } from "@/entities/course";
import { http } from "@/shared/api";

export interface ClientPortalAppointment {
  id: string;
  startDate: string;
  endDate: string;
  status: AppointmentStatus;
  courseTheme?: { id: string; title: string } | null;
}

export const clientPortalApi = {
  schedule(params: { timezone: string }) {
    return http
      .get<{ nextAppointment: ClientPortalAppointment | null }>("/client-portal/schedule", { params })
      .then((response) => response.data.nextAppointment);
  },
  courseEnrollments() {
    return http.get<{ enrollments: CourseEnrollment[] }>("/client-portal/course-enrollments").then((response) => response.data.enrollments);
  },
};
