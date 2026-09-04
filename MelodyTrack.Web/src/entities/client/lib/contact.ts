import { AsYouType, parsePhoneNumberFromString } from "libphonenumber-js/max";

export function normalizeSocialLink(value: string | null | undefined, type: "telegram" | "vk") {
  const handle = extractSocialHandle(value, type);
  if (!handle) {
    return undefined;
  }

  return type === "telegram" ? `https://t.me/${handle}` : `https://vk.com/${handle}`;
}

export function getSocialLinkHref(value: string | null | undefined, type: "telegram" | "vk") {
  const handle = extractSocialHandle(value, type);
  if (!handle) {
    return undefined;
  }

  return type === "telegram" ? `tg://resolve?domain=${handle}` : `https://vk.com/${handle}`;
}

export function getSocialHandle(value: string | null | undefined, type: "telegram" | "vk") {
  return extractSocialHandle(value, type) ?? "";
}

export function normalizePhone(value?: string | null) {
  const phoneNumber = parsePhoneNumber(value);
  if (!phoneNumber?.isValid()) {
    return undefined;
  }

  return phoneNumber.number;
}

export function getPhoneUri(value?: string | null) {
  const phoneNumber = parsePhoneNumber(value);
  if (!phoneNumber?.isValid()) {
    return undefined;
  }

  return phoneNumber.getURI();
}

export function getPhoneDigits(value?: string | null) {
  return value?.replace(/\D/g, "") ?? "";
}

export function hasPhoneDigits(value?: string | null) {
  return getPhoneDigits(value).length > 0;
}

export function isValidPhone(value?: string | null) {
  return Boolean(normalizePhone(value));
}

export function formatPhone(value?: string | null) {
  const prepared = preparePhoneInput(value);
  if (!prepared) {
    return "";
  }

  const asYouType = new AsYouType();
  return asYouType.input(prepared);
}

export function formatPhoneInput(value?: string | null) {
  const prepared = preparePhoneInput(value);
  if (!prepared) {
    return "";
  }

  const asYouType = new AsYouType();
  return asYouType.input(prepared);
}

function isValidSocialHandle(handle: string, type: "telegram" | "vk") {
  return type === "telegram" ? /^[a-zA-Z0-9_]{5,32}$/.test(handle) : /^[a-zA-Z0-9_.]{3,64}$/.test(handle);
}

function extractSocialHandle(value: string | null | undefined, type: "telegram" | "vk") {
  const raw = value?.trim();
  if (!raw) {
    return undefined;
  }

  const withoutProtocol = raw.replace(/^(?:https?:\/\/|tg:\/\/resolve\?domain=)/i, "").replace(/^@/, "");
  const withoutWww = withoutProtocol.replace(/^www\./i, "");
  const hostPattern = type === "telegram" ? /^(?:t\.me|telegram\.me)\//i : /^(?:vk\.com|vk\.ru)\//i;
  const path = withoutWww.replace(hostPattern, "").split(/[?#&]/)[0].replace(/^@/, "");
  const handle = path.split("/")[0]?.trim();

  if (!handle || !isValidSocialHandle(handle, type)) {
    return undefined;
  }

  return handle;
}

function preparePhoneInput(value?: string | null) {
  const raw = value?.trim();
  if (!raw) {
    return undefined;
  }

  const normalized = raw.startsWith("00") ? `+${raw.slice(2)}` : raw;
  const digits = normalized.replace(/\D/g, "");

  if (digits.length === 0) {
    return normalized.startsWith("+") ? "+" : undefined;
  }

  return `+${digits}`;
}

function parsePhoneNumber(value?: string | null) {
  const prepared = preparePhoneInput(value);
  if (!prepared) {
    return undefined;
  }

  return parsePhoneNumberFromString(prepared);
}
