import dayjs from "dayjs";
import { describe, expect, it } from "vitest";

import type { UserAvailability } from "../model/types";
import { getBlockedRanges, isSlotAvailable } from "./availability";

const day = dayjs("2030-06-03T00:00:00");

const availability: UserAvailability = {
  userId: "01JTESTUSER000000000000000",
  workingHours: [{ dayOfWeek: "monday", isWorkingDay: true, startTime: "09:00", endTime: "18:00" }],
  vacations: [
    {
      id: "01JTESTVACATION00000000000",
      startDate: day.hour(12).minute(30).toISOString(),
      endDate: day.hour(14).minute(15).toISOString(),
    },
  ],
};

describe("timed user availability", () => {
  it("blocks only appointment slots that overlap a partial-day vacation", () => {
    expect(isSlotAvailable(availability, day.hour(12).minute(0), 60)).toBe(false);
    expect(isSlotAvailable(availability, day.hour(14).minute(15), 60)).toBe(true);
  });

  it("renders the exact vacation segment inside the visible day", () => {
    const vacationRange = getBlockedRanges(availability, day, 9, 18).find((range) => range.isVacation);

    expect(vacationRange).toEqual({
      startMinute: 12 * 60 + 30,
      endMinute: 14 * 60 + 15,
      isVacation: true,
      vacationId: "01JTESTVACATION00000000000",
    });
  });

  it("uses only existing working minutes when the teacher's clock moves forward", () => {
    const spring: UserAvailability = {
      ...availability,
      timezone: "Europe/Berlin",
      vacations: [],
      workingHours: [{ dayOfWeek: "sunday", isWorkingDay: true, startTime: "02:30", endTime: "04:00" }],
    };

    expect(isSlotAvailable(spring, dayjs("2030-03-31T01:00:00Z"), 60)).toBe(true);
    expect(() => getBlockedRanges(spring, dayjs("2030-03-31T00:00:00"), 0, 24)).not.toThrow();
  });

  it("blocks a lesson crossing the gap between repeated working windows", () => {
    const autumn: UserAvailability = {
      ...availability,
      timezone: "Europe/Berlin",
      vacations: [],
      workingHours: [{ dayOfWeek: "sunday", isWorkingDay: true, startTime: "02:00", endTime: "02:30" }],
    };

    expect(isSlotAvailable(autumn, dayjs("2030-10-27T00:00:00Z"), 30)).toBe(true);
    expect(isSlotAvailable(autumn, dayjs("2030-10-27T01:00:00Z"), 30)).toBe(true);
    expect(isSlotAvailable(autumn, dayjs("2030-10-27T00:20:00Z"), 60)).toBe(false);
  });

  it("allows a lesson ending exactly at midnight when working hours end at 24:00", () => {
    const late: UserAvailability = {
      ...availability,
      timezone: "UTC",
      vacations: [],
      workingHours: [{ dayOfWeek: "monday", isWorkingDay: true, startTime: "22:00", endTime: "24:00" }],
    };

    expect(isSlotAvailable(late, dayjs("2030-06-03T23:00:00Z"), 60)).toBe(true);
  });
});
