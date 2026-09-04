import { http } from "@/shared/api";

import type { ClientsReport, DashboardStats, FinanceReport, ReportParams, WorkReport } from "../model/types";

export const dashboardApi = {
  stats(timezone: string) {
    return http.get<DashboardStats>("/dashboard", { params: { timezone } }).then((response) => response.data);
  },
  work(params: ReportParams) {
    return http.get<WorkReport>("/reports/work", { params }).then((response) => response.data);
  },
  finance(params: ReportParams) {
    return http.get<FinanceReport>("/reports/finance", { params }).then((response) => response.data);
  },
  clients(params: ReportParams) {
    return http.get<ClientsReport>("/reports/clients", { params }).then((response) => response.data);
  },
};
