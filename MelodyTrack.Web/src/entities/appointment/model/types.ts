import type { RecordActivity, Ulid } from "@/shared/api";

export type AppointmentStatus = "planned" | "completed" | "cancelled" | "burned";
export type AppointmentMutationScope = "single" | "this-and-following" | "all" | "weekday-this-and-following" | "weekday-all";

export interface AppointmentClientContacts {
  id?: Ulid | null;
  telegram?: string | null;
  vk?: string | null;
  phone?: string | null;
}

export interface AppointmentClient {
  id: Ulid;
  firstName: string;
  lastName: string;
  patronymic?: string | null;
  contacts?: AppointmentClientContacts | null;
}

export interface AppointmentService {
  id: Ulid;
  name: string;
}

export interface AppointmentProvider {
  id: Ulid;
  firstName: string;
  lastName: string;
  roleDisplayName: string;
}

export interface AppointmentCourseTheme {
  id: Ulid;
  title: string;
  courseId: Ulid;
  courseName: string;
}

export interface AppointmentRecurrenceRule {
  id: Ulid;
  startDate: string;
  endDate?: string | null;
  key: string;
  recurrencePattern?: number | null;
}

export interface Appointment {
  id: Ulid;
  client: AppointmentClient;
  service: AppointmentService;
  provider?: AppointmentProvider | null;
  courseTheme?: AppointmentCourseTheme | null;
  lessonNotes?: string | null;
  startDate: string;
  endDate: string;
  status: AppointmentStatus;
  recurringRule?: AppointmentRecurrenceRule | null;
  lastActivity?: RecordActivity | null;
}

export interface RecurrenceType {
  id: Ulid;
  key: string;
  displayName: string;
}

export type ListAppointmentsParams = {
  timezone: string;
  startDate: string;
  endDate: string;
};

export type CreateAppointmentInput = {
  clientId: Ulid;
  serviceId: Ulid;
  providerId?: Ulid | null;
  courseThemeId?: Ulid | null;
  recurrenceTypeId?: Ulid | null;
  lessonNotes?: string | null;
  startDate: string;
  timezone: string;
  patternEndDate?: string | null;
  recurrencePattern?: number | null;
};

export type UpdateAppointmentInput = Partial<{
  clientId: Ulid | null;
  serviceId: Ulid | null;
  providerId: Ulid | null;
  courseThemeId: Ulid | null;
  hasCourseThemeSelection: boolean;
  recurrenceTypeId: Ulid | null;
  lessonNotes: string | null;
  hasLessonNotes: boolean;
  startDate: string | null;
  timezone: string | null;
  status: AppointmentStatus | null;
  scope: AppointmentMutationScope | null;
  recurrencePattern: number | null;
  expectedActivityId: Ulid;
}>;

export type DeleteAppointmentInput = {
  scope?: AppointmentMutationScope | null;
  expectedActivityId?: Ulid;
};
