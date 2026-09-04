import type { ApiJsonContract, RecordActivity, RequiredApiContract, Ulid } from "@/shared/api";
import type {
  AppointmentClientContactsDto,
  AppointmentClientDto,
  AppointmentCourseThemeDto,
  AppointmentDto,
  AppointmentProviderDto,
  AppointmentRecurrenceRuleDto,
  AppointmentServiceDto,
  CreateAppointmentRequest,
  LookupRecurrenceTypeDto,
  UpdateAppointmentRequest,
} from "@/shared/api/generated/models";

export type AppointmentStatus = "planned" | "completed" | "cancelled" | "burned";
export type AppointmentMutationScope = "single" | "this-and-following" | "all" | "weekday-this-and-following" | "weekday-all";

export type AppointmentClientContacts = ApiJsonContract<AppointmentClientContactsDto>;
export type AppointmentClient = Omit<RequiredApiContract<AppointmentClientDto, "id" | "firstName" | "lastName">, "contacts"> & {
  contacts?: AppointmentClientContacts | null;
};
export type AppointmentService = RequiredApiContract<AppointmentServiceDto, "id" | "name" | "isTrial">;
export type AppointmentProvider = RequiredApiContract<AppointmentProviderDto, "id" | "firstName" | "lastName" | "roleDisplayName">;
export type AppointmentCourseTheme = RequiredApiContract<AppointmentCourseThemeDto, "id" | "title" | "courseId" | "courseName">;
export type AppointmentRecurrenceRule = RequiredApiContract<AppointmentRecurrenceRuleDto, "id" | "startDate" | "key">;
export type Appointment = Omit<
  RequiredApiContract<AppointmentDto, "id" | "client" | "service" | "startDate" | "endDate" | "status">,
  "client" | "service" | "provider" | "courseTheme" | "recurringRule" | "lastActivity" | "status"
> & {
  client: AppointmentClient;
  service: AppointmentService;
  provider?: AppointmentProvider | null;
  courseTheme?: AppointmentCourseTheme | null;
  recurringRule?: AppointmentRecurrenceRule | null;
  lastActivity?: RecordActivity | null;
  status: AppointmentStatus;
};
export type RecurrenceType = RequiredApiContract<LookupRecurrenceTypeDto, "id" | "key" | "displayName">;

export type ListAppointmentsParams = {
  timezone: string;
  startDate: string;
  endDate: string;
};

export type CreateAppointmentInput = RequiredApiContract<CreateAppointmentRequest, "clientId" | "serviceId" | "startDate" | "timezone">;

export type UpdateAppointmentInput = Omit<ApiJsonContract<UpdateAppointmentRequest>, "status" | "scope"> & {
  status?: AppointmentStatus | null;
  scope?: AppointmentMutationScope | null;
};

export type DeleteAppointmentInput = {
  scope?: AppointmentMutationScope | null;
  expectedActivityId?: Ulid;
};
