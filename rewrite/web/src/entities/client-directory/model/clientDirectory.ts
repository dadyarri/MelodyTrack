import { DateOnly } from "@microsoft/kiota-abstractions";
import { invalidResponse, isObject } from "@/shared/api";

export type DirectoryContact = {
  id: string;
  kindId: string;
  kindName: string;
  format: "phone" | "email" | "link";
  value: string;
  comment: string | null;
  isPrimary: boolean;
};
export type DirectoryClient = {
  id: string;
  firstName: string;
  lastName: string;
  middleName: string | null;
  birthdate: string | null;
  sourceId: string | null;
  sourceName: string | null;
  isArchived: boolean;
  createdAt: string;
  contacts: DirectoryContact[];
};
export type DirectoryPage = {
  items: DirectoryClient[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};
export type DirectoryOptions = {
  sources: { id: string; name: string }[];
  contactKinds: {
    id: string;
    name: string;
    format: "phone" | "email" | "link";
    isArchived: boolean;
  }[];
};
export type DirectoryQuery = {
  q: string;
  archive: "active" | "archived" | "all";
  sourceId: string;
  withoutSource: boolean;
  sort: string;
  page: number;
  pageSize: number;
};
export const initialDirectoryQuery: DirectoryQuery = {
  q: "",
  archive: "active",
  sourceId: "",
  withoutSource: false,
  sort: "name:asc",
  page: 1,
  pageSize: 10,
};
function text(value: unknown, location: string): string {
  if (typeof value !== "string" || !value.length) throw invalidResponse(location);
  return value;
}
function optionalText(value: unknown, location: string): string | null {
  if (value === null || value === undefined) return null;
  if (typeof value !== "string") throw invalidResponse(location);
  return value;
}
function boolean(value: unknown, location: string): boolean {
  if (typeof value !== "boolean") throw invalidResponse(location);
  return value;
}
function number(value: unknown, minimum: number, location: string): number {
  if (typeof value !== "number" || !Number.isSafeInteger(value) || value < minimum)
    throw invalidResponse(location);
  return value;
}
function date(value: unknown): string | null {
  if (value === null || value === undefined) return null;
  const result = value instanceof DateOnly ? value.toString() : value;
  if (typeof result !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(result))
    throw invalidResponse("clients.items[].birthdate");
  return result;
}
function format(value: unknown, location: string): DirectoryContact["format"] {
  if (value !== "phone" && value !== "email" && value !== "link") throw invalidResponse(location);
  return value;
}
export function readDirectoryPage(value: unknown): DirectoryPage {
  if (!isObject(value) || !Array.isArray(value.items)) throw invalidResponse("clients.items");
  const page = number(value.page, 1, "clients.page"),
    pageSize = number(value.pageSize, 1, "clients.pageSize"),
    totalCount = number(value.totalCount, 0, "clients.totalCount"),
    totalPages = number(value.totalPages, 0, "clients.totalPages");
  if (totalPages !== Math.ceil(totalCount / pageSize) || value.items.length > pageSize)
    throw invalidResponse("clients.pagination");
  const items = value.items.map((item: unknown): DirectoryClient => {
    if (!isObject(item) || !Array.isArray(item.contacts))
      throw invalidResponse("clients.items[].contacts");
    const createdAt =
      item.createdAt instanceof Date
        ? Number.isFinite(item.createdAt.getTime())
          ? item.createdAt.toISOString()
          : null
        : text(item.createdAt, "clients.items[].createdAt");
    if (!createdAt || !Number.isFinite(Date.parse(createdAt)))
      throw invalidResponse("clients.items[].createdAt");
    return {
      id: text(item.id, "clients.items[].id"),
      firstName: text(item.firstName, "clients.items[].firstName"),
      lastName: text(item.lastName, "clients.items[].lastName"),
      middleName: optionalText(item.middleName, "clients.items[].middleName"),
      birthdate: date(item.birthdate),
      sourceId: optionalText(item.sourceId, "clients.items[].sourceId"),
      sourceName: optionalText(item.sourceName, "clients.items[].sourceName"),
      isArchived: boolean(item.isArchived, "clients.items[].isArchived"),
      createdAt,
      contacts: item.contacts.map((entry: unknown): DirectoryContact => {
        if (!isObject(entry)) throw invalidResponse("clients.items[].contacts[]");
        return {
          id: text(entry.id, "clients.items[].contacts[].id"),
          kindId: text(entry.kindId, "clients.items[].contacts[].kindId"),
          kindName: text(entry.kindName, "clients.items[].contacts[].kindName"),
          format: format(entry.format, "clients.items[].contacts[].format"),
          value: text(entry.value, "clients.items[].contacts[].value"),
          comment: optionalText(entry.comment, "clients.items[].contacts[].comment"),
          isPrimary: boolean(entry.isPrimary, "clients.items[].contacts[].isPrimary"),
        };
      }),
    };
  });
  return { items, page, pageSize, totalCount, totalPages };
}
export function readDirectoryOptions(value: unknown): DirectoryOptions {
  if (!isObject(value) || !Array.isArray(value.sources) || !Array.isArray(value.contactKinds))
    throw invalidResponse("clients.options");
  const contactKinds = value.contactKinds;
  return {
    sources: value.sources.map((item: unknown) => {
      if (!isObject(item)) throw invalidResponse("clients.options.sources[]");
      return {
        id: text(item.id, "clients.options.sources[].id"),
        name: text(item.name, "clients.options.sources[].name"),
      };
    }),
    contactKinds: contactKinds.map((item: unknown) => {
      if (!isObject(item)) throw invalidResponse("clients.options.contactKinds[]");
      const kindFormat = format(item.format, "clients.options.contactKinds[].format");
      return {
        id: text(item.id, "clients.options.contactKinds[].id"),
        name: text(item.name, "clients.options.contactKinds[].name"),
        format: kindFormat,
        isArchived: boolean(item.isArchived, "clients.options.contactKinds[].isArchived"),
      };
    }),
  };
}
export function clientName(client: DirectoryClient): string {
  return [client.lastName, client.firstName, client.middleName].filter(Boolean).join(" ");
}
