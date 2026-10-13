import dayjs, { type Dayjs } from "dayjs";
import timezone from "dayjs/plugin/timezone";
import utc from "dayjs/plugin/utc";

dayjs.extend(utc);
dayjs.extend(timezone);
export const browserTimezone = Intl.DateTimeFormat().resolvedOptions().timeZone;
export function toTeacherTime(value: string | Date | Dayjs, zone: string) {
  return dayjs(value).tz(zone);
}
export function teacherTimeToUtc(value: Dayjs, zone: string) {
  const wallTime = value.format("YYYY-MM-DD HH:mm:ss");
  let converted = dayjs.tz(wallTime, zone);
  if (!converted.isValid() || converted.format("YYYY-MM-DD HH:mm:ss") !== wallTime)
    throw new Error("Выбранное время отсутствует при переходе на летнее время. Выберите другой час.");
  // Choose the later instant for an ambiguous wall time, matching the server's standard offset.
  const first = converted;
  for (const minutes of [30, 60, 90, 120]) {
    const alternative = first.add(minutes, "minute").tz(zone);
    if (alternative.format("YYYY-MM-DD HH:mm:ss") === wallTime) converted = alternative;
  }
  return converted.toISOString();
}

export function getTeacherWorkingIntervals(date: string, startTime: string, endTime: string, zone: string) {
  const boundary = (time: string, earliest: boolean) => {
    let wall = dayjs.utc(`${date}T${time}`);
    let instant = dayjs.tz(wall.format("YYYY-MM-DD HH:mm:ss"), zone);
    while (instant.format("YYYY-MM-DD HH:mm:ss") !== wall.format("YYYY-MM-DD HH:mm:ss")) {
      wall = wall.add(1, "minute");
      instant = dayjs.tz(wall.format("YYYY-MM-DD HH:mm:ss"), zone);
    }
    const first = instant;
    for (const minutes of [30, 60, 90, 120]) {
      const alternative = first.add(earliest ? -minutes : minutes, "minute").tz(zone);
      if (alternative.format("YYYY-MM-DD HH:mm:ss") === wall.format("YYYY-MM-DD HH:mm:ss")) instant = alternative;
    }
    return instant;
  };
  const start = boundary(startTime, true);
  const end = boundary(endTime, false);
  if (start.valueOf() >= end.valueOf()) return [];
  if (start.utcOffset() === end.utcOffset()) return [{ start, end }];

  const minuteOfDay = (time: string) => {
    const [hours, minutes] = time.split(":").map(Number);
    return hours * 60 + minutes;
  };
  const intervals: Array<{ start: Dayjs; end: Dayjs }> = [];
  for (let instant = start.utc(); instant.valueOf() < end.valueOf(); instant = instant.add(1, "minute")) {
    const local = instant.tz(zone);
    const minute = local.hour() * 60 + local.minute();
    if (local.format("YYYY-MM-DD") !== date || minute < minuteOfDay(startTime) || minute >= minuteOfDay(endTime)) continue;
    const previous = intervals.at(-1);
    if (previous?.end.valueOf() === instant.valueOf()) previous.end = instant.add(1, "minute");
    else intervals.push({ start: instant, end: instant.add(1, "minute") });
  }
  return intervals;
}
