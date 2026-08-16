import { afterEach, describe, expect, it, vi } from "vitest";

import { http } from "@/shared/api";

import { getReleaseHistory } from "./releaseApi";

describe("getReleaseHistory", () => {
  afterEach(() => vi.restoreAllMocks());

  it("uses the bounded public history endpoint", async () => {
    const data = validHistory();
    const get = vi.spyOn(http, "get").mockResolvedValue({ data });

    await expect(getReleaseHistory(2)).resolves.toEqual(data);
    expect(get).toHaveBeenCalledWith("/releases", { params: { page: 2, page_size: 2 } });
  });

  it("rejects malformed release data", async () => {
    vi.spyOn(http, "get").mockResolvedValue({ data: { ...validHistory(), currentVersion: 1 } });
    await expect(getReleaseHistory()).rejects.toThrow();
  });
});

function validHistory() {
  return {
    currentVersion: "2026.07.1",
    releases: [
      {
        version: "2026.07.1",
        codename: "Accordatura",
        date: "2026-07-29",
        changes: { new: ["История обновлений"], improved: [], fixed: [], security: [] },
        parentVersion: null,
      },
    ],
    page: 1,
    pageSize: 2,
    totalCount: 1,
    totalPages: 1,
    hasNextPage: false,
  };
}
