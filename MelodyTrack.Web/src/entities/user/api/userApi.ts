import { http, type Ulid } from "@/shared/api";

import type { CalendarSubscription, Role, User, UserAvailability, UserWorkingHoursDay } from "../model/types";

export const usersApi = {
  list() {
    return http.get<{ users: User[] }>("/users").then((response) => response.data.users);
  },
  update(
    id: Ulid,
    input: { firstName: string; lastName: string; phone?: string; telegram?: string; vk?: string },
    options?: { expectedActivityId?: Ulid },
  ) {
    return http.patch<unknown>(`/users/${id}`, { ...input, expectedActivityId: options?.expectedActivityId }).then(() => undefined);
  },
  listAvailabilities() {
    return http.get<{ availabilities: UserAvailability[] }>("/users/availability").then((response) => response.data.availabilities);
  },
  getAvailability(id: Ulid) {
    return http.get<UserAvailability>(`/users/${id}/availability`).then((response) => response.data);
  },
  updateAvailability(
    id: Ulid,
    input: { workingHours: UserWorkingHoursDay[]; vacations: Array<{ startDate: string; endDate: string }> },
    options?: { expectedActivityId?: Ulid },
  ) {
    return http
      .put<unknown>(`/users/${id}/availability`, { ...input, expectedActivityId: options?.expectedActivityId })
      .then(() => undefined);
  },
};

export const rolesApi = {
  lookup() {
    return http.get<{ roles: Role[] }>("/roles/options").then((response) => response.data.roles);
  },
};

export const calendarSubscriptionsApi = {
  regenerateUser(userId: Ulid) {
    return http.post<CalendarSubscription>(`/users/${userId}/calendar-subscriptions`, {}).then((response) => response.data);
  },
};
