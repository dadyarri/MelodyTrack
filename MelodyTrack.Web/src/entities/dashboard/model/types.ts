import type { RequiredApiContract, Ulid } from "@/shared/api";
import type {
  ClientActivityTrendDto,
  ClientSourceReportDto,
  ClientsReportResponse,
  ClientsReportSummaryDto,
  ClientValueReportDto,
  DashboardAppointmentResponse,
  DashboardClientContactsResponse,
  DashboardClientResponse,
  DashboardScheduleDayResponse,
  DashboardServiceResponse,
  FinanceDebtorDto,
  FinanceExpenseCategoryDto,
  FinanceReportResponse,
  FinanceReportSummaryDto,
  FinanceServiceDto,
  FinanceTrendDto,
  GetDashboardStatsResponse,
  OrganizationDashboardResponse,
  ReportContextDto,
  ReportProviderDto,
  WorkHourDto,
  WorkProviderDto,
  WorkReportResponse,
  WorkReportSummaryDto,
  WorkServiceDto,
  WorkStatusDto,
  WorkTrendDto,
} from "@/shared/api/generated/models";

type AppointmentStatus = "planned" | "completed" | "cancelled" | "burned";

export type OrganizationDashboardStats = RequiredApiContract<
  OrganizationDashboardResponse,
  | "totalClients"
  | "debtorsCount"
  | "totalDebt"
  | "totalPositiveBalance"
  | "appointmentsToday"
  | "appointmentsTomorrow"
  | "monthIncome"
  | "monthExpenses"
  | "monthNet"
>;
export type DashboardAppointmentContacts = RequiredApiContract<DashboardClientContactsResponse, never>;
export type DashboardAppointmentClient = Omit<RequiredApiContract<DashboardClientResponse, "id" | "firstName" | "lastName">, "contacts"> & {
  contacts?: DashboardAppointmentContacts | null;
};
export type DashboardAppointmentService = RequiredApiContract<DashboardServiceResponse, "id" | "name">;
export type DashboardAppointment = Omit<
  RequiredApiContract<DashboardAppointmentResponse, "id" | "client" | "service" | "startDate" | "endDate" | "status">,
  "client" | "service" | "status"
> & {
  client: DashboardAppointmentClient;
  service: DashboardAppointmentService;
  status: AppointmentStatus;
};
export type DashboardScheduleDay = Omit<
  RequiredApiContract<DashboardScheduleDayResponse, "date" | "count" | "appointments">,
  "appointments"
> & {
  appointments: DashboardAppointment[];
};
export type DashboardStats = Omit<
  RequiredApiContract<GetDashboardStatsResponse, "personalClientsCount" | "monthIncome" | "today" | "tomorrow">,
  "today" | "tomorrow" | "organization"
> & {
  today: DashboardScheduleDay;
  tomorrow: DashboardScheduleDay;
  organization?: OrganizationDashboardStats | null;
};

export type ReportGroupBy = "day" | "week" | "month";

export interface ReportParams {
  timezone: string;
  start: string;
  end: string;
  providerId?: Ulid;
  groupBy: ReportGroupBy;
}

export type ReportProvider = RequiredApiContract<ReportProviderDto, "id" | "displayName">;
export type ReportContext = Omit<
  RequiredApiContract<ReportContextDto, "startDate" | "endDate" | "timezone" | "scopeLabel" | "groupBy" | "providers">,
  "groupBy" | "providers"
> & {
  groupBy: ReportGroupBy;
  providers: ReportProvider[];
};

type WorkSummary = RequiredApiContract<
  WorkReportSummaryDto,
  "appointments" | "completed" | "burned" | "workingCapacityHours" | "occupiedWorkingHours" | "freeWorkingHours"
>;
type WorkStatus = Omit<RequiredApiContract<WorkStatusDto, "status" | "count">, "status"> & { status: AppointmentStatus };
type WorkTrend = RequiredApiContract<
  WorkTrendDto,
  | "startDate"
  | "endDate"
  | "appointments"
  | "completed"
  | "cancelled"
  | "burned"
  | "workingCapacityHours"
  | "occupiedWorkingHours"
  | "freeWorkingHours"
>;
type WorkProvider = RequiredApiContract<
  WorkProviderDto,
  | "providerName"
  | "appointments"
  | "completed"
  | "cancelled"
  | "burned"
  | "workingCapacityHours"
  | "occupiedWorkingHours"
  | "freeWorkingHours"
>;
type WorkService = RequiredApiContract<WorkServiceDto, "serviceId" | "serviceName" | "appointments" | "completed" | "burned" | "revenue">;
type WorkHour = RequiredApiContract<WorkHourDto, "hour" | "appointments" | "completed" | "cancelled">;
export type WorkReport = Omit<
  RequiredApiContract<WorkReportResponse, "context" | "summary" | "statuses" | "trend" | "providers" | "services" | "busyHours">,
  "context" | "summary" | "statuses" | "trend" | "providers" | "services" | "busyHours"
> & {
  context: ReportContext;
  summary: WorkSummary;
  statuses: WorkStatus[];
  trend: WorkTrend[];
  providers: WorkProvider[];
  services: WorkService[];
  busyHours: WorkHour[];
};

type FinanceSummary = RequiredApiContract<
  FinanceReportSummaryDto,
  "revenue" | "forecastIncome" | "forecastAppointments" | "revenueAppointments" | "organizationOnlyFiguresAvailable"
>;
type FinanceTrend = RequiredApiContract<FinanceTrendDto, "startDate" | "endDate" | "revenue">;
type FinanceExpenseCategory = RequiredApiContract<FinanceExpenseCategoryDto, "categoryName" | "amount">;
type FinanceDebtor = RequiredApiContract<FinanceDebtorDto, "clientId" | "clientName" | "revenue" | "payments" | "debt">;
type FinanceService = RequiredApiContract<FinanceServiceDto, "serviceId" | "serviceName" | "appointments" | "revenue">;
export type FinanceReport = Omit<
  RequiredApiContract<FinanceReportResponse, "context" | "summary" | "trend" | "expenseCategories" | "debtors" | "services">,
  "context" | "summary" | "trend" | "expenseCategories" | "debtors" | "services"
> & {
  context: ReportContext;
  summary: FinanceSummary;
  trend: FinanceTrend[];
  expenseCategories: FinanceExpenseCategory[];
  debtors: FinanceDebtor[];
  services: FinanceService[];
};

type ClientsSummary = RequiredApiContract<
  ClientsReportSummaryDto,
  "acquiredClients" | "activeClients" | "retainedClients" | "atRiskClients" | "lostClients" | "onVacationClients"
>;
type ClientTrend = RequiredApiContract<ClientActivityTrendDto, "startDate" | "endDate" | "acquiredClients" | "activeClients" | "visits">;
type ClientSource = RequiredApiContract<ClientSourceReportDto, "sourceName" | "acquiredClients" | "activeClients" | "clientValue">;
type ClientValue = Omit<
  RequiredApiContract<ClientValueReportDto, "clientId" | "clientName" | "sourceName" | "visits" | "value" | "activityState">,
  "activityState"
> & {
  activityState: "active" | "inactive" | "at-risk" | "lost" | "on-vacation";
};
export type ClientsReport = Omit<
  RequiredApiContract<ClientsReportResponse, "context" | "summary" | "trend" | "sources" | "clients">,
  "context" | "summary" | "trend" | "sources" | "clients"
> & {
  context: ReportContext;
  summary: ClientsSummary;
  trend: ClientTrend[];
  sources: ClientSource[];
  clients: ClientValue[];
};
