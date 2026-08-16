import type { RecordActivity, Ulid } from "@/shared/api";

export interface User {
  id: Ulid;
  firstName: string;
  lastName: string;
  roleDisplayName: string;
  telegram?: string | null;
  vk?: string | null;
  phone?: string | null;
  lastActivity?: RecordActivity | null;
}

export type WeekdayKey = "monday" | "tuesday" | "wednesday" | "thursday" | "friday" | "saturday" | "sunday";

export interface UserWorkingHoursDay {
  dayOfWeek: WeekdayKey;
  isWorkingDay: boolean;
  startTime?: string | null;
  endTime?: string | null;
}

export interface UserVacation {
  id: Ulid;
  startDate: string;
  endDate: string;
}

export interface UserAvailability {
  userId: Ulid;
  workingHours: UserWorkingHoursDay[];
  vacations: UserVacation[];
  lastActivity?: RecordActivity | null;
}

export interface Role {
  id: Ulid;
  displayName: string;
}

export interface CalendarSubscription {
  id: Ulid;
  token: string;
  url: string;
  feedType: "user" | "client";
}
