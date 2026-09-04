import type { ApiJsonContract, PaginatedParams, PaginatedResponse, RecordActivity, RequiredApiContract, Ulid } from "@/shared/api";
import type {
  CalendarSubscriptionResponse,
  ClientFinancialHistoryEventDto,
  ClientHistoryContactsDto,
  ClientHistoryResponse,
  ClientHistorySummaryDto,
  ClientVacation as GeneratedClientVacation,
  ClientWithBalanceDto,
  CreateClientRequest,
  LookupClientDto,
  UpdateClientRequest,
} from "@/shared/api/generated/models";

export type ClientLifecycleStatus = 0 | 1 | 2 | 3;

export type ClientContacts = ApiJsonContract<ClientHistoryContactsDto>;
export type ClientVacation = RequiredApiContract<GeneratedClientVacation, "clientId" | "startDate" | "endDate">;
export type Client = Omit<
  RequiredApiContract<
    ClientWithBalanceDto,
    "id" | "firstName" | "lastName" | "createdAtUtc" | "isLeadClosed" | "vacations" | "balance" | "lifecycleStatus"
  >,
  "appointments" | "source" | "vacations" | "lastActivity" | "lifecycleStatus"
> & {
  vacations: ClientVacation[];
  lifecycleStatus: ClientLifecycleStatus;
  lastActivity?: RecordActivity | null;
};
export type ClientWithBalance = Client;
export type ClientHistorySummary = RequiredApiContract<
  ClientHistorySummaryDto,
  "totalPayments" | "paymentsCount" | "completedAppointmentsCount" | "upcomingAppointmentsCount"
>;

export type ClientFinancialHistoryEventType = "top_up" | "appointment";
export type ClientHistoryAppointmentStatus = "planned" | "completed" | "cancelled" | "burned";

export type ClientFinancialHistoryEvent = Omit<
  RequiredApiContract<ClientFinancialHistoryEventDto, "id" | "type" | "amount" | "date">,
  "type" | "appointmentStatus"
> & {
  type: ClientFinancialHistoryEventType;
  appointmentStatus?: ClientHistoryAppointmentStatus | null;
};
export type ClientHistory = Omit<
  RequiredApiContract<ClientHistoryResponse, "client" | "summary" | "events">,
  "client" | "summary" | "events"
> & {
  client: Client;
  summary: ClientHistorySummary;
  events: PaginatedResponse<ClientFinancialHistoryEvent>;
};
export type LookupClient = Omit<RequiredApiContract<LookupClientDto, "id" | "firstName" | "lastName">, "contacts"> & {
  contacts?: ClientContacts | null;
};

export type CreateClientInput = RequiredApiContract<CreateClientRequest, "firstName" | "lastName">;
export type UpdateClientInput = ApiJsonContract<UpdateClientRequest>;

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

export type ClientCalendarSubscription = RequiredApiContract<CalendarSubscriptionResponse, "id" | "token" | "url" | "feedType"> & {
  feedType: "client";
};
