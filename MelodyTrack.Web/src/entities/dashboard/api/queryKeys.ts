import type { ReportParams } from "../model/types";

export const analyticsQueryKeys = {
  all: ["dashboard"] as const,
  stats: (timezone: string) => ["dashboard", "stats", timezone] as const,
  report: (area: "work" | "finance" | "clients", params: ReportParams) => ["reports", area, params] as const,
};
