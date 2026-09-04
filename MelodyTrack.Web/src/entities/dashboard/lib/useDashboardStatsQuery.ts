import dayjs from "dayjs";

import type { ReportGroupBy, ReportParams } from "../model/types";

export function getDefaultReportParams(): ReportParams {
  return {
    timezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
    start: dayjs().startOf("month").format("YYYY-MM-DD"),
    end: dayjs().endOf("month").format("YYYY-MM-DD"),
    groupBy: "month",
  };
}

export function parseReportParams(search: URLSearchParams): ReportParams {
  const defaults = getDefaultReportParams();
  const groupBy = search.get("groupBy");
  return {
    timezone: search.get("timezone") || defaults.timezone,
    start: validDate(search.get("start")) ?? defaults.start,
    end: validDate(search.get("end")) ?? defaults.end,
    providerId: search.get("providerId") || undefined,
    groupBy: isGroupBy(groupBy) ? groupBy : defaults.groupBy,
  };
}

export function serializeReportParams(params: ReportParams) {
  const search = new URLSearchParams({
    timezone: params.timezone,
    start: params.start,
    end: params.end,
    groupBy: params.groupBy,
  });
  if (params.providerId) {
    search.set("providerId", params.providerId);
  }
  return search;
}

function validDate(value: string | null) {
  return value && dayjs(value, "YYYY-MM-DD", true).isValid() ? value : null;
}

function isGroupBy(value: string | null): value is ReportGroupBy {
  return value === "day" || value === "week" || value === "month";
}

export type { ReportGroupBy };
