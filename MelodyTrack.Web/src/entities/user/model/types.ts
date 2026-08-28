import type { RecordActivity, RequiredApiContract } from "@/shared/api";
import type {
  CalendarSubscriptionResponse,
  GetUsersDto,
  LookupRolesDto,
  UserAvailabilityResponse,
  UserVacationDto,
  UserWorkingHoursDayDto,
} from "@/shared/api/generated/models";

export type User = Omit<RequiredApiContract<GetUsersDto, "id" | "firstName" | "lastName" | "roleDisplayName">, "lastActivity"> & {
  lastActivity?: RecordActivity | null;
};

export type WeekdayKey = "monday" | "tuesday" | "wednesday" | "thursday" | "friday" | "saturday" | "sunday";

export type UserWorkingHoursDay = Omit<RequiredApiContract<UserWorkingHoursDayDto, "dayOfWeek" | "isWorkingDay">, "dayOfWeek"> & {
  dayOfWeek: WeekdayKey;
};

export type UserVacation = RequiredApiContract<UserVacationDto, "id" | "startDate" | "endDate">;

export type UserAvailability = Omit<
  RequiredApiContract<UserAvailabilityResponse, "userId" | "workingHours" | "vacations">,
  "workingHours" | "vacations" | "lastActivity"
> & {
  workingHours: UserWorkingHoursDay[];
  vacations: UserVacation[];
  lastActivity?: RecordActivity | null;
};

export type Role = RequiredApiContract<LookupRolesDto, "id" | "displayName">;

export type CalendarSubscription = Omit<
  RequiredApiContract<CalendarSubscriptionResponse, "id" | "token" | "url" | "feedType">,
  "feedType"
> & {
  feedType: "user" | "client";
};
