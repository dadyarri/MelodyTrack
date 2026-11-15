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
function text(value: unknown): string {
  if (typeof value !== "string" || !value.length) throw invalidResponse();
  return value;
}
function optionalText(value: unknown): string | null {
  return value === null || value === undefined ? null : text(value);
}
function boolean(value: unknown): boolean {
  if (typeof value !== "boolean") throw invalidResponse();
  return value;
}
function number(value: unknown, minimum: number): number {
  if (typeof value !== "number" || !Number.isSafeInteger(value) || value < minimum)
    throw invalidResponse();
  return value;
}
function date(value: unknown): string | null {
  if (value === null || value === undefined) return null;
  const result = value instanceof DateOnly ? value.toString() : value;
  if (typeof result !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(result)) throw invalidResponse();
  return result;
}
function format(value: unknown): DirectoryContact["format"] {
  if (value !== "phone" && value !== "email" && value !== "link") throw invalidResponse();
  return value;
}
export function readDirectoryPage(value: unknown): DirectoryPage {
  if (!isObject(value) || !Array.isArray(value.items)) throw invalidResponse();
  const page = number(value.page, 1),
    pageSize = number(value.pageSize, 1),
    totalCount = number(value.totalCount, 0),
    totalPages = number(value.totalPages, 0);
  if (totalPages !== Math.ceil(totalCount / pageSize) || value.items.length > pageSize)
    throw invalidResponse();
  const items = value.items.map((item: unknown): DirectoryClient => {
    if (!isObject(item) || !Array.isArray(item.contacts)) throw invalidResponse();
    const createdAt =
      item.createdAt instanceof Date ? item.createdAt.toISOString() : text(item.createdAt);
    if (!Number.isFinite(Date.parse(createdAt))) throw invalidResponse();
    return {
      id: text(item.id),
      firstName: text(item.firstName),
      lastName: text(item.lastName),
      middleName: optionalText(item.middleName),
      birthdate: date(item.birthdate),
      sourceId: optionalText(item.sourceId),
      sourceName: optionalText(item.sourceName),
      isArchived: boolean(item.isArchived),
      createdAt,
      contacts: item.contacts.map((entry: unknown): DirectoryContact => {
        if (!isObject(entry)) throw invalidResponse();
        return {
          id: text(entry.id),
          kindId: text(entry.kindId),
          kindName: text(entry.kindName),
          format: format(entry.format),
          value: text(entry.value),
          comment: optionalText(entry.comment),
          isPrimary: boolean(entry.isPrimary),
        };
      }),
    };
  });
  return { items, page, pageSize, totalCount, totalPages };
}
export function readDirectoryOptions(value: unknown): DirectoryOptions {
  if (!isObject(value) || !Array.isArray(value.sources)) throw invalidResponse();
  return {
    sources: value.sources.map((item: unknown) => {
      if (!isObject(item)) throw invalidResponse();
      return { id: text(item.id), name: text(item.name) };
    }),
  };
}
export function clientName(client: DirectoryClient): string {
  return [client.lastName, client.firstName, client.middleName].filter(Boolean).join(" ");
}
