export type CalendarDate = { year: number; month: number; day: number };

export function daysInMonth(year: number, month: number): number {
  if (month === 2) return year % 400 === 0 || (year % 4 === 0 && year % 100 !== 0) ? 29 : 28;
  return [4, 6, 9, 11].includes(month) ? 30 : 31;
}

export function parseDate(value: string | null | undefined): CalendarDate | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value ?? "");
  if (!match) return null;
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  return year >= 1 &&
    year <= 9999 &&
    month >= 1 &&
    month <= 12 &&
    day >= 1 &&
    day <= daysInMonth(year, month)
    ? { year, month, day }
    : null;
}

export function dateValue(date: CalendarDate): string {
  return `${String(date.year).padStart(4, "0")}-${String(date.month).padStart(2, "0")}-${String(date.day).padStart(2, "0")}`;
}

export function dateAsUtc(date: CalendarDate): Date {
  const result = new Date(0);
  result.setUTCFullYear(date.year, date.month - 1, date.day);
  result.setUTCHours(0, 0, 0, 0);
  return result;
}

export function todayDate(): CalendarDate {
  const parts = new Intl.DateTimeFormat("en", {
    timeZone: "Europe/Moscow",
    calendar: "gregory",
    numberingSystem: "latn",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).formatToParts(new Date());
  return {
    year: Number(parts.find((part) => part.type === "year")?.value),
    month: Number(parts.find((part) => part.type === "month")?.value),
    day: Number(parts.find((part) => part.type === "day")?.value),
  };
}

export function addDays(date: CalendarDate, count: number): CalendarDate | null {
  const result = dateAsUtc(date);
  result.setUTCDate(result.getUTCDate() + count);
  const value = {
    year: result.getUTCFullYear(),
    month: result.getUTCMonth() + 1,
    day: result.getUTCDate(),
  };
  return value.year >= 1 && value.year <= 9999 ? value : null;
}

export function addMonths(date: CalendarDate, count: number): CalendarDate | null {
  const monthIndex = date.year * 12 + date.month - 1 + count;
  const year = Math.floor(monthIndex / 12);
  const month = monthIndex - year * 12 + 1;
  return year >= 1 && year <= 9999
    ? { year, month, day: Math.min(date.day, daysInMonth(year, month)) }
    : null;
}

export function withinDateRange(value: string, min?: string, max?: string): boolean {
  return (!parseDate(min) || value >= (min ?? "")) && (!parseDate(max) || value <= (max ?? ""));
}

export function clampDate(date: CalendarDate, min?: string, max?: string): CalendarDate {
  const lower = parseDate(min);
  const upper = parseDate(max);
  if (lower && dateValue(date) < dateValue(lower)) return lower;
  if (upper && dateValue(date) > dateValue(upper)) return upper;
  return date;
}

export function displayDate(value: string | null): string {
  const date = parseDate(value);
  return date
    ? `${String(date.day).padStart(2, "0")}.${String(date.month).padStart(2, "0")}.${String(date.year).padStart(4, "0")}`
    : "";
}

export function parseDateInput(value: string): string | null {
  const text = value.trim();
  if (parseDate(text)) return text;
  const match = /^(\d{1,2})\.(\d{1,2})\.(\d{4})$/.exec(text);
  if (!match) return null;
  const result = `${match[3]}-${String(match[2]).padStart(2, "0")}-${String(match[1]).padStart(2, "0")}`;
  return parseDate(result) ? result : null;
}

export function parseTime(
  value: string | null | undefined,
): { hour: string; minute: string } | null {
  const match = /^([01]\d|2[0-3]):([0-5]\d)$/.exec(value ?? "");
  return match ? { hour: match[1] ?? "", minute: match[2] ?? "" } : null;
}

export function parseDateTime(
  value: string | null | undefined,
): { date: string; time: string } | null {
  const match = /^(\d{4}-\d{2}-\d{2})T(\d{2}:\d{2})$/.exec(value ?? "");
  const date = match?.[1];
  const time = match?.[2];
  return date && time && parseDate(date) && parseTime(time) ? { date, time } : null;
}
