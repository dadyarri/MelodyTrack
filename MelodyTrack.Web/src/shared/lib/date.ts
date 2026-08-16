import dayjs, { type Dayjs } from "dayjs";

export const DATE_FORMAT = "DD.MM.YYYY";
export const DATE_TIME_FORMAT = "DD.MM.YYYY HH:mm";
export const TIME_FORMAT = "HH:mm";

export function formatDate(value: string | Date | Dayjs) {
  return dayjs(value).format(DATE_FORMAT);
}

export function formatDateTime(value: string | Date | Dayjs) {
  return dayjs(value).format(DATE_TIME_FORMAT);
}
