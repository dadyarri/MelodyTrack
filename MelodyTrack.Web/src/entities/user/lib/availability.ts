import dayjs, { type Dayjs } from "dayjs";

import type { UserAvailability, WeekdayKey } from "../model/types";

export type AvailabilityBlockedRange = {
  startMinute: number;
  endMinute: number;
  isVacation: boolean;
  vacationId?: string;
};

export const weekdayOrder: WeekdayKey[] = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

export const weekdayLabels: Record<WeekdayKey, string> = {
  monday: "Понедельник",
  tuesday: "Вторник",
  wednesday: "Среда",
  thursday: "Четверг",
  friday: "Пятница",
  saturday: "Суббота",
  sunday: "Воскресенье",
};

export function isSlotAvailable(availability: UserAvailability | null | undefined, startDate: Dayjs, durationMinutes = 60) {
  if (!availability) {
    return true;
  }

  const endDate = startDate.add(durationMinutes, "minute");
  const overlapsVacation = availability.vacations.some(
    (vacation) => startDate.isBefore(dayjs(vacation.endDate)) && endDate.isAfter(dayjs(vacation.startDate)),
  );
  if (overlapsVacation) {
    return false;
  }

  const dayKey = getWeekdayKey(startDate);
  const workingDay = availability.workingHours.find((item) => item.dayOfWeek === dayKey);
  if (!workingDay) {
    return true;
  }

  if (!workingDay.isWorkingDay || !workingDay.startTime || !workingDay.endTime) {
    return false;
  }

  const startMinute = startDate.hour() * 60 + startDate.minute();
  const endMinute = startMinute + durationMinutes;
  return startMinute >= parseTimeToMinutes(workingDay.startTime) && endMinute <= parseTimeToMinutes(workingDay.endTime);
}

export function getBlockedRanges(availability: UserAvailability | null | undefined, day: Dayjs, startHour: number, endHour: number) {
  if (!availability) {
    return [];
  }

  const dayStart = startHour * 60;
  const dayEnd = endHour * 60;
  const ranges: AvailabilityBlockedRange[] = [];

  const visibleStart = day.startOf("day").add(dayStart, "minute");
  const visibleEnd = day.startOf("day").add(dayEnd, "minute");
  const vacationRanges = availability.vacations.flatMap((vacation) => {
    const start = dayjs(vacation.startDate);
    const end = dayjs(vacation.endDate);
    if (!start.isBefore(visibleEnd) || !end.isAfter(visibleStart)) {
      return [];
    }

    return [
      {
        startMinute: Math.max(dayStart, start.isAfter(visibleStart) ? start.diff(day.startOf("day"), "minute") : dayStart),
        endMinute: Math.min(dayEnd, end.isBefore(visibleEnd) ? end.diff(day.startOf("day"), "minute") : dayEnd),
        isVacation: true,
        vacationId: vacation.id,
      },
    ];
  });

  const dayKey = getWeekdayKey(day);
  const workingDay = availability.workingHours.find((item) => item.dayOfWeek === dayKey);
  if (!workingDay) {
    return vacationRanges;
  }

  if (!workingDay.isWorkingDay || !workingDay.startTime || !workingDay.endTime) {
    return [...vacationRanges, { startMinute: dayStart, endMinute: dayEnd, isVacation: false }];
  }

  const workStart = parseTimeToMinutes(workingDay.startTime);
  const workEnd = parseTimeToMinutes(workingDay.endTime);

  if (workStart > dayStart) {
    ranges.push({ startMinute: dayStart, endMinute: workStart, isVacation: false });
  }

  if (workEnd < dayEnd) {
    ranges.push({ startMinute: workEnd, endMinute: dayEnd, isVacation: false });
  }

  return [...ranges, ...vacationRanges].filter((range) => range.endMinute > range.startMinute);
}

export function getVisibleScheduleHours(
  availabilities: Array<UserAvailability | null | undefined>,
  fallback = { startHour: 10, endHour: 21 },
) {
  const workingRanges = availabilities.flatMap((availability) =>
    (availability?.workingHours ?? []).flatMap((item) => {
      if (!item.isWorkingDay || !item.startTime || !item.endTime) {
        return [];
      }

      return [
        {
          startMinute: parseTimeToMinutes(item.startTime),
          endMinute: parseTimeToMinutes(item.endTime),
        },
      ];
    }),
  );

  if (workingRanges.length === 0) {
    return fallback;
  }

  const minStartMinute = Math.min(...workingRanges.map((item) => item.startMinute));
  const maxEndMinute = Math.max(...workingRanges.map((item) => item.endMinute));

  return {
    startHour: Math.max(0, Math.floor(minStartMinute / 60)),
    endHour: Math.min(24, Math.max(Math.floor(minStartMinute / 60) + 1, Math.ceil(maxEndMinute / 60))),
  };
}

function getWeekdayKey(day: Dayjs): WeekdayKey {
  switch (day.day()) {
    case 1:
      return "monday";
    case 2:
      return "tuesday";
    case 3:
      return "wednesday";
    case 4:
      return "thursday";
    case 5:
      return "friday";
    case 6:
      return "saturday";
    default:
      return "sunday";
  }
}

function parseTimeToMinutes(value: string) {
  const [hours = "0", minutes = "0"] = value.split(":");
  return Number(hours) * 60 + Number(minutes);
}
