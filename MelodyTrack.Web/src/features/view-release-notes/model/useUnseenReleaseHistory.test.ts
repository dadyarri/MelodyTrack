import { describe, expect, it, vi } from "vitest";

import type { ReleaseEntry, ReleaseHistory } from "@/entities/release";

import { loadUnseenReleaseHistory } from "./useUnseenReleaseHistory";

const current = createRelease("2026.07.1.1");
const previous = createRelease("2026.07.1");
const older = createRelease("2026.06.1");

describe("loadUnseenReleaseHistory", () => {
  it("loads newer entries across pages until the acknowledged version", async () => {
    const loadPage = vi
      .fn<(page: number) => Promise<ReleaseHistory>>()
      .mockResolvedValueOnce(createHistory([current], { page: 1, hasNextPage: true }))
      .mockResolvedValueOnce(createHistory([previous, older], { page: 2, hasNextPage: false }));

    await expect(loadUnseenReleaseHistory(older.version, loadPage)).resolves.toEqual({
      currentVersion: current.version,
      releases: [current, previous],
    });
    expect(loadPage).toHaveBeenCalledTimes(2);
  });

  it("returns every available entry when there is no acknowledgement", async () => {
    const loadPage = vi.fn<(page: number) => Promise<ReleaseHistory>>().mockResolvedValue(createHistory([current, previous, older]));

    await expect(loadUnseenReleaseHistory(null, loadPage)).resolves.toEqual({
      currentVersion: current.version,
      releases: [current, previous, older],
    });
  });

  it("stops immediately when the current version was acknowledged", async () => {
    const loadPage = vi.fn<(page: number) => Promise<ReleaseHistory>>().mockResolvedValue(createHistory([current, previous]));

    await expect(loadUnseenReleaseHistory(current.version, loadPage)).resolves.toEqual({
      currentVersion: current.version,
      releases: [],
    });
    expect(loadPage).toHaveBeenCalledOnce();
  });
});

function createRelease(version: string): ReleaseEntry {
  return {
    version,
    codename: "Accordatura",
    date: "2026-07-29",
    parentVersion: version.split(".").length === 4 ? "2026.07.1" : null,
    changes: { new: [version], improved: [], fixed: [], security: [] },
  };
}

function createHistory(releases: ReleaseEntry[], pagination: Partial<Pick<ReleaseHistory, "page" | "hasNextPage">> = {}): ReleaseHistory {
  return {
    currentVersion: current.version,
    releases,
    page: 1,
    pageSize: 2,
    totalCount: releases.length,
    totalPages: 1,
    hasNextPage: false,
    ...pagination,
  };
}
