import type { PaginatedParams, PaginatedResponse, RecordActivity, Ulid } from "@/shared/api";

export type ClientLifecycleStatus = 0 | 1 | 2 | 3;

export interface ClientContacts {
  id?: Ulid;
  email?: string | null;
  telegram?: string | null;
  vk?: string | null;
  phone?: string | null;
}

export interface ClientVacation {
  clientId: Ulid;
  startDate: string;
  endDate: string;
}

export interface Client {
  id: Ulid;
  firstName: string;
  lastName: string;
  patronymic?: string | null;
  dateOfBirth?: string | null;
  contacts?: ClientContacts | null;
  telegram?: string | null;
  vk?: string | null;
  phone?: string | null;
  sourceId?: Ulid | null;
  sourceName?: string | null;
  createdAtUtc: string;
  isLeadClosed: boolean;
  vacations: ClientVacation[];
  balance: number;
  lastAppointmentAtUtc?: string | null;
  nextAppointmentAtUtc?: string | null;
  lifecycleStatus: ClientLifecycleStatus;
  lastActivity?: RecordActivity | null;
}

export interface ClientWithBalance extends Client {
  balance: number;
}

export interface ClientHistorySummary {
  totalPayments: number;
  paymentsCount: number;
  completedAppointmentsCount: number;
  upcomingAppointmentsCount: number;
  lastPaymentAtUtc?: string | null;
  lastVisitAtUtc?: string | null;
  nextAppointmentAtUtc?: string | null;
}

export type ClientFinancialHistoryEventType = "top_up" | "appointment";
export type ClientHistoryAppointmentStatus = "planned" | "completed" | "cancelled" | "burned";

export interface ClientFinancialHistoryEvent {
  id: Ulid;
  type: ClientFinancialHistoryEventType;
  amount: number;
  date: string;
  description?: string | null;
  serviceName?: string | null;
  providerDisplayName?: string | null;
  appointmentStatus?: ClientHistoryAppointmentStatus | null;
}

export interface ClientHistory {
  client: Client;
  summary: ClientHistorySummary;
  events: PaginatedResponse<ClientFinancialHistoryEvent>;
}

export interface LookupClient {
  id: Ulid;
  firstName: string;
  lastName: string;
  patronymic?: string | null;
  contacts?: ClientContacts | null;
  sourceId?: Ulid | null;
  sourceName?: string | null;
}

export type CreateClientInput = {
  firstName: string;
  lastName: string;
  patronymic?: string | null;
  dateOfBirth?: string | null;
  email?: string | null;
  telegram?: string;
  vk?: string;
  phone?: string;
  sourceId?: Ulid;
};

export type UpdateClientInput = Partial<Omit<CreateClientInput, "sourceId">> & {
  sourceId?: Ulid | null;
  vacations?: Array<{ startDate: string; endDate: string }>;
};

export type ListClientsParams = PaginatedParams & {
  search?: string;
  lifecycleStatus?: ClientLifecycleStatus;
  firstName?: string;
  lastName?: string;
  dateOfBirth?: string;
  sourceId?: Ulid;
  createdAtUtc?: string;
  isLeadClosed?: boolean;
};

export type GetClientHistoryParams = PaginatedParams & {
  expectedActivityId?: Ulid;
};

export type ClientCalendarSubscription = {
  id: Ulid;
  token: string;
  url: string;
  feedType: "client";
};
