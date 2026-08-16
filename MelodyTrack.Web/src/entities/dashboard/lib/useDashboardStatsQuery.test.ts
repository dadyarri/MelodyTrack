import { describe, expect, it } from "vitest";

import { parseReportParams, serializeReportParams } from "./useDashboardStatsQuery";

describe("report URL filters", () => {
  it("keeps the shared period, timezone, grouping and teacher filter", () => {
    const source = new URLSearchParams({
      start: "2026-07-01",
      end: "2026-07-31",
      timezone: "Europe/Moscow",
      providerId: "01K12345678901234567890123",
      groupBy: "week",
    });

    const parsed = parseReportParams(source);

    expect(parsed).toEqual({
      start: "2026-07-01",
      end: "2026-07-31",
      timezone: "Europe/Moscow",
      providerId: "01K12345678901234567890123",
      groupBy: "week",
    });
    expect(parseReportParams(serializeReportParams(parsed))).toEqual(parsed);
  });

  it("omits the teacher filter when no teacher is selected", () => {
    const serialized = serializeReportParams({
      start: "2026-07-01",
      end: "2026-07-31",
      timezone: "UTC",
      groupBy: "day",
    });

    expect(serialized.has("providerId")).toBe(false);
  });
});
