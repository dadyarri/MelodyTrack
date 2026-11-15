import { DateOnly } from "@microsoft/kiota-abstractions";
import { describe, expect, it } from "vitest";
import { readReleaseHistory } from "./release";

const changes = {
  newEscaped: ["Полная переработка системы"],
  improved: [],
  fixed: [],
  security: [],
};
const entry = {
  version: "2026.12.1",
  codename: "Da capo",
  codenameMeaning: "С начала, заново.",
  date: null,
  parentVersion: null,
  changes,
};
const history = {
  currentVersion: "2026.12.1",
  releases: [entry],
  page: 1,
  pageSize: 2,
  totalCount: 1,
  totalPages: 1,
  hasNextPage: false,
};

describe("release contract", () => {
  it("keeps an undated current version without inventing a publication date", () => {
    const result = readReleaseHistory(history);

    expect(result.releases[0]?.date).toBeNull();
    expect(result.releases[0]?.changes.new).toEqual(["Полная переработка системы"]);
  });

  it("reads publication dates parsed by the generated Kiota model", () => {
    const result = readReleaseHistory({
      ...history,
      releases: [{ ...entry, date: new DateOnly({ year: 2026, month: 12, day: 1 }) }],
    });

    expect(result.releases[0]?.date).toBe("2026-12-01");
  });

  it("rejects inconsistent pagination instead of silently truncating history", () => {
    expect(() => readReleaseHistory({ ...history, totalCount: 3 })).toThrow();
    expect(() => readReleaseHistory({ ...history, hasNextPage: true })).toThrow();
  });
});
