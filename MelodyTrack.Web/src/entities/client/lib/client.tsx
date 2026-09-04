import type { Client, ClientHistory, ClientHistoryAppointmentStatus } from "../model/types";
import { formatPhone, getPhoneUri, getSocialHandle, getSocialLinkHref } from "./contact";

export function formatClientName(client: Pick<Client, "firstName" | "lastName" | "patronymic">) {
  return [client.lastName, client.firstName, client.patronymic].filter(Boolean).join(" ");
}

export function getClientContactValue(client: Client, key: "telegram" | "vk" | "phone") {
  return client[key] ?? undefined;
}

export function renderClientPhoneLink(value?: string | null) {
  const uri = getPhoneUri(value);
  if (!uri) {
    return null;
  }

  return <a href={uri}>{formatPhone(value)}</a>;
}

export function renderClientSocialLink(value: string | null | undefined, type: "telegram" | "vk") {
  const href = getSocialLinkHref(value, type);
  const handle = getSocialHandle(value, type);
  if (!href || !handle) {
    return null;
  }

  return (
    <a href={href} target="_blank" rel="noreferrer">
      @{handle}
    </a>
  );
}

export function renderClientHistoryAppointmentStatus(status: NonNullable<ClientHistory["events"]["items"][number]["appointmentStatus"]>) {
  return getClientHistoryAppointmentStatusLabel(status);
}

function getClientHistoryAppointmentStatusLabel(status: ClientHistoryAppointmentStatus) {
  switch (status) {
    case "completed":
      return "Завершено";
    case "cancelled":
      return "Отменено";
    case "burned":
      return "Сгорело";
    default:
      return "Запланировано";
  }
}
