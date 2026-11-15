import { DateOnly } from "@microsoft/kiota-abstractions";
import { invalidResponse, isObject } from "@/shared/api";

export const releaseVersionPattern = /^\d{4}\.(?:0[1-9]|1[0-2])\.[1-9]\d*(?:\.[1-9]\d*)?$/;

export type ReleaseChanges = {
  new: string[];
  improved: string[];
  fixed: string[];
  security: string[];
};
export type ReleaseEntry = {
  version: string;
  codename: string | null;
  codenameMeaning: string | null;
  date: string | null;
  parentVersion: string | null;
  changes: ReleaseChanges;
};
export type CurrentRelease = {
  version: string;
  codename: string | null;
  codenameMeaning: string | null;
};
export type ReleaseHistory = {
  currentVersion: string;
  releases: ReleaseEntry[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNextPage: boolean;
};

function version(value: unknown): string {
  if (typeof value !== "string" || !releaseVersionPattern.test(value)) throw invalidResponse();
  return value;
}

function optionalText(value: unknown): string | null {
  if (value === null || value === undefined) return null;
  if (typeof value !== "string") throw invalidResponse();
  return value;
}

function entries(value: unknown): string[] {
  if (!Array.isArray(value) || value.some((entry) => typeof entry !== "string" || !entry.trim()))
    throw invalidResponse();
  return value as string[];
}

function date(value: unknown): string | null {
  if (value === null || value === undefined) return null;
  const result = value instanceof DateOnly ? value.toString() : value;
  if (typeof result !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(result)) throw invalidResponse();
  return result;
}

export function readCurrentRelease(value: unknown): CurrentRelease {
  if (!isObject(value)) throw invalidResponse();
  return {
    version: version(value.version),
    codename: optionalText(value.codename),
    codenameMeaning: optionalText(value.codenameMeaning),
  };
}

export function readReleaseHistory(value: unknown): ReleaseHistory {
  if (!isObject(value) || !Array.isArray(value.releases)) throw invalidResponse();
  const { page, pageSize, totalCount, totalPages, hasNextPage } = value;
  if (
    typeof page !== "number" ||
    !Number.isSafeInteger(page) ||
    page < 1 ||
    typeof pageSize !== "number" ||
    !Number.isSafeInteger(pageSize) ||
    pageSize < 1 ||
    typeof totalCount !== "number" ||
    !Number.isSafeInteger(totalCount) ||
    totalCount < 0 ||
    typeof totalPages !== "number" ||
    !Number.isSafeInteger(totalPages) ||
    totalPages < 0 ||
    typeof hasNextPage !== "boolean" ||
    totalPages !== Math.ceil(totalCount / pageSize) ||
    hasNextPage !== page < totalPages ||
    value.releases.length > pageSize
  )
    throw invalidResponse();
  const releases = value.releases.map((entry: unknown): ReleaseEntry => {
    if (!isObject(entry) || !isObject(entry.changes)) throw invalidResponse();
    return {
      version: version(entry.version),
      codename: optionalText(entry.codename),
      codenameMeaning: optionalText(entry.codenameMeaning),
      date: date(entry.date),
      parentVersion:
        entry.parentVersion === null || entry.parentVersion === undefined
          ? null
          : version(entry.parentVersion),
      changes: {
        new: entries(entry.changes.newEscaped),
        improved: entries(entry.changes.improved),
        fixed: entries(entry.changes.fixed),
        security: entries(entry.changes.security),
      },
    };
  });
  return {
    currentVersion: version(value.currentVersion),
    releases,
    page,
    pageSize,
    totalCount,
    totalPages,
    hasNextPage,
  };
}
