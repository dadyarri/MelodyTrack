import { afterEach, describe, expect, it, vi } from "vitest";
import {
  addDays,
  addMonths,
  dateValue,
  parseDate,
  parseDateInput,
  parseDateTime,
  parseTime,
  todayDate,
  withinDateRange,
} from "./calendar";

afterEach(() => vi.useRealTimers());

describe("date and time values", () => {
  it("uses Moscow midnight for today regardless of the device timezone", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-11-11T20:59:59Z"));
    expect(todayDate()).toEqual({ year: 2026, month: 11, day: 11 });
    vi.setSystemTime(new Date("2026-11-11T21:00:00Z"));
    expect(todayDate()).toEqual({ year: 2026, month: 11, day: 12 });
  });

  it("accepts real Gregorian dates and rejects rollover and invalid leap days", () => {
    expect(parseDateInput("29.02.2024")).toBe("2024-02-29");
    expect(parseDateInput("29.02.2026")).toBeNull();
    expect(parseDate("2026-04-31")).toBeNull();
    expect(parseDate("0000-01-01")).toBeNull();
    expect(parseDateInput("12.11.2026")).toBe("2026-11-12");
  });

  it("moves dates across years and clamps month navigation to a real day", () => {
    const december = { year: 2026, month: 12, day: 31 };
    expect(addDays(december, 1)).toEqual({ year: 2027, month: 1, day: 1 });
    expect(addMonths({ year: 2024, month: 1, day: 31 }, 1)).toEqual({
      year: 2024,
      month: 2,
      day: 29,
    });
    expect(addDays({ year: 1, month: 1, day: 1 }, -1)).toBeNull();
    expect(dateValue({ year: 1, month: 1, day: 1 })).toBe("0001-01-01");
  });

  it("treats min and max as inclusive calendar dates", () => {
    expect(withinDateRange("2026-11-12", "2026-11-12", "2026-11-12")).toBe(true);
    expect(withinDateRange("2026-11-11", "2026-11-12", "2026-11-20")).toBe(false);
    expect(withinDateRange("2026-11-21", "2026-11-12", "2026-11-20")).toBe(false);
  });

  it("preserves wall-clock values without accepting a timezone or invalid time", () => {
    expect(parseDateTime("2026-11-12T09:30")).toEqual({ date: "2026-11-12", time: "09:30" });
    expect(parseDateTime("2026-11-12T09:30Z")).toBeNull();
    expect(parseDateTime("2026-11-12T24:00")).toBeNull();
    expect(parseTime("23:59")).toEqual({ hour: "23", minute: "59" });
    expect(parseTime("09:60")).toBeNull();
  });
});
