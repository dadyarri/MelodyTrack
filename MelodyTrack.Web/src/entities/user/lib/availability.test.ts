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
});
