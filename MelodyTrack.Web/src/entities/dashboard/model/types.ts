import type { Ulid } from "@/shared/api";

type AppointmentStatus = "planned" | "completed" | "cancelled" | "burned";

export interface DashboardStats {
  personalClientsCount: number;
  monthIncome: number;
  today: DashboardScheduleDay;
  tomorrow: DashboardScheduleDay;
  organization?: OrganizationDashboardStats | null;
}

export interface OrganizationDashboardStats {
  totalClients: number;
  debtorsCount: number;
  totalDebt: number;
  totalPositiveBalance: number;
  appointmentsToday: number;
  appointmentsTomorrow: number;
  monthIncome: number;
  monthExpenses: number;
  monthNet: number;
}

export interface DashboardScheduleDay {
  date: string;
  count: number;
  appointments: DashboardAppointment[];
}

export interface DashboardAppointment {
  id: Ulid;
  client: DashboardAppointmentClient;
  service: DashboardAppointmentService;
  startDate: string;
  endDate: string;
  status: AppointmentStatus;
}

export interface DashboardAppointmentClient {
  id: Ulid;
  firstName: string;
  lastName: string;
  contacts?: DashboardAppointmentContacts | null;
}

export interface DashboardAppointmentContacts {
  telegram?: string | null;
  vk?: string | null;
  phone?: string | null;
}

export interface DashboardAppointmentService {
  id: Ulid;
  name: string;
}

export type ReportGroupBy = "day" | "week" | "month";

export interface ReportParams {
  timezone: string;
  start: string;
  end: string;
  providerId?: Ulid;
  groupBy: ReportGroupBy;
}

export interface ReportContext {
  startDate: string;
  endDate: string;
  timezone: string;
  providerId?: Ulid | null;
  scopeLabel: string;
  groupBy: ReportGroupBy;
  providers: ReportProvider[];
}

export interface ReportProvider {
  id: Ulid;
  displayName: string;
}

export interface WorkReport {
  context: ReportContext;
  summary: {
    appointments: number;
    completed: number;
    burned: number;
    workingCapacityHours: number;
    occupiedWorkingHours: number;
    freeWorkingHours: number;
    utilizationPercent?: number | null;
    cancellationPercent?: number | null;
  };
  statuses: Array<{ status: AppointmentStatus; count: number; sharePercent?: number | null }>;
  trend: Array<{
    startDate: string;
    endDate: string;
    appointments: number;
    completed: number;
    cancelled: number;
    burned: number;
    workingCapacityHours: number;
    occupiedWorkingHours: number;
    freeWorkingHours: number;
    utilizationPercent?: number | null;
  }>;
  providers: Array<{
    providerId?: Ulid | null;
    providerName: string;
    appointments: number;
    completed: number;
    cancelled: number;
    burned: number;
    workingCapacityHours: number;
    occupiedWorkingHours: number;
    freeWorkingHours: number;
    utilizationPercent?: number | null;
  }>;
  services: Array<{
    serviceId: Ulid;
    serviceName: string;
    appointments: number;
    completed: number;
    burned: number;
    revenue: number;
  }>;
  busyHours: Array<{ hour: number; appointments: number; completed: number; cancelled: number }>;
}

export interface FinanceReport {
  context: ReportContext;
  summary: {
    revenue: number;
    payments?: number | null;
    expenses?: number | null;
    netProfit?: number | null;
    outstandingDebt?: number | null;
    averageRevenuePerVisit?: number | null;
    revenueAppointments: number;
    organizationOnlyFiguresAvailable: boolean;
  };
  trend: Array<{
    startDate: string;
    endDate: string;
    revenue: number;
    payments?: number | null;
    expenses?: number | null;
    netProfit?: number | null;
  }>;
  expenseCategories: Array<{ categoryName: string; amount: number }>;
  debtors: Array<{ clientId: Ulid; clientName: string; revenue: number; payments: number; debt: number }>;
  services: Array<{ serviceId: Ulid; serviceName: string; appointments: number; revenue: number }>;
}

export interface ClientsReport {
  context: ReportContext;
  summary: {
    acquiredClients: number;
    activeClients: number;
    retainedClients: number;
    retentionPercent?: number | null;
    atRiskClients: number;
    lostClients: number;
    onVacationClients: number;
    averageVisitFrequency?: number | null;
    averageClientValue?: number | null;
  };
  trend: Array<{ startDate: string; endDate: string; acquiredClients: number; activeClients: number; visits: number }>;
  sources: Array<{ sourceName: string; acquiredClients: number; activeClients: number; clientValue: number }>;
  clients: Array<{
    clientId: Ulid;
    clientName: string;
    sourceName: string;
    visits: number;
    value: number;
    averageIntervalDays?: number | null;
    lastVisitAtUtc?: string | null;
    activityState: "active" | "inactive" | "at-risk" | "lost" | "on-vacation";
  }>;
}
