import { type CreateEntityResponse, http, type Ulid } from "@/shared/api";

import type {
  Appointment,
  CreateAppointmentInput,
  DeleteAppointmentInput,
  ListAppointmentsParams,
  RecurrenceType,
  UpdateAppointmentInput,
} from "../model/types";

export const appointmentsApi = {
  list(params: ListAppointmentsParams) {
    return http.get<{ appointments: Appointment[] }>("/appointments", { params }).then((response) => response.data.appointments);
  },
  recurrenceTypes() {
    return http
      .get<{ recurrenceTypes: RecurrenceType[] }>("/appointment-recurrence-types/options")
      .then((response) => response.data.recurrenceTypes);
  },
  create(input: CreateAppointmentInput, options?: { idempotencyKey?: string; signal?: AbortSignal }) {
    return http.post<CreateEntityResponse>("/appointments", input, buildIdempotencyConfig(options)).then((response) => response.data);
  },
  update(id: Ulid, input: UpdateAppointmentInput) {
    return http.patch<unknown>(`/appointments/${id}`, input).then(() => undefined);
  },
  remove(id: Ulid, input: DeleteAppointmentInput = {}) {
    return http
      .delete<unknown>(`/appointments/${id}`, {
        data: input,
      })
      .then(() => undefined);
  },
};

function buildIdempotencyConfig(options?: { idempotencyKey?: string; signal?: AbortSignal }) {
  if (!options?.idempotencyKey && !options?.signal) {
    return undefined;
  }

  return {
    signal: options.signal,
    headers: options.idempotencyKey
      ? {
          "Idempotency-Key": options.idempotencyKey,
        }
      : undefined,
  };
}
