import { http, type RequiredApiContract, type Ulid } from "@/shared/api";
import type { GetUsersAvailabilityResponse, GetUsersResponse, LookupRolesResponse } from "@/shared/api/generated/models";

import type { CalendarSubscription, Role, User, UserAvailability, UserWorkingHoursDay } from "../model/types";

type UsersResponse = Omit<RequiredApiContract<GetUsersResponse, "users">, "users"> & { users: User[] };
type UserAvailabilitiesResponse = Omit<RequiredApiContract<GetUsersAvailabilityResponse, "availabilities">, "availabilities"> & {
  availabilities: UserAvailability[];
};
type RolesResponse = Omit<RequiredApiContract<LookupRolesResponse, "roles">, "roles"> & { roles: Role[] };

export const usersApi = {
  list() {
    return http.get<UsersResponse>("/users").then((response) => response.data.users);
  },
  update(
    id: Ulid,
    input: { firstName: string; lastName: string; phone?: string; telegram?: string; vk?: string },
    options?: { expectedActivityId?: Ulid },
  ) {
    return http.patch<unknown>(`/users/${id}`, { ...input, expectedActivityId: options?.expectedActivityId }).then(() => undefined);
  },
  listAvailabilities() {
    return http.get<UserAvailabilitiesResponse>("/users/availability").then((response) => response.data.availabilities);
  },
  getAvailability(id: Ulid) {
    return http.get<UserAvailability>(`/users/${id}/availability`).then((response) => response.data);
  },
  getVacationAppointmentConflictCount(id: Ulid, startDate: string, endDate: string, signal?: AbortSignal) {
    return http
      .get<number>(`/users/${id}/vacation-appointment-conflict-count`, {
        params: { startDate, endDate },
        signal,
      })
      .then((response) => response.data);
  },
  updateAvailability(
    id: Ulid,
    input: {
      workingHours: UserWorkingHoursDay[];
      vacations: Array<{ startDate: string; endDate: string }>;
      cancelConflictingAppointments?: boolean;
    },
    options?: { expectedActivityId?: Ulid },
  ) {
    return http
      .put<unknown>(`/users/${id}/availability`, { ...input, expectedActivityId: options?.expectedActivityId })
      .then(() => undefined);
  },
};

export const rolesApi = {
  lookup() {
    return http.get<RolesResponse>("/roles/options").then((response) => response.data.roles);
  },
};

export const calendarSubscriptionsApi = {
  regenerateUser(userId: Ulid) {
    return http.post<CalendarSubscription>(`/users/${userId}/calendar-subscriptions`, {}).then((response) => response.data);
  },
};
