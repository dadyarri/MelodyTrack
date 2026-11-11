import { describe, expect, it, vi } from "vitest";
import type { ReleaseEntry, ReleaseHistory } from "@/entities/app-release";
import { loadUnseenReleases } from "./unseenReleases";

const release = (version: string): ReleaseEntry => ({ version, codename: null, codenameMeaning: null, date: null, parentVersion: null, changes: { new: [], improved: [], fixed: [], security: [] } });
const page = (number: number, versions: string[], totalCount = 5): ReleaseHistory => ({
  currentVersion: "2026.12.3", releases: versions.map(release), page: number, pageSize: 2,
  totalCount, totalPages: Math.ceil(totalCount / 2), hasNextPage: number < Math.ceil(totalCount / 2),
});

describe("unseen release history", () => {
  it("collects changes across pages up to the acknowledged version", async () => {
    const load = vi.fn().mockResolvedValueOnce(page(1, ["2026.12.3", "2026.12.2"]))
      .mockResolvedValueOnce(page(2, ["2026.12.1", "2026.11.2"]));

    const result = await loadUnseenReleases("2026.11.2", load);

    expect(result.releases.map((entry) => entry.version)).toEqual(["2026.12.3", "2026.12.2", "2026.12.1"]);
    expect(load.mock.calls).toEqual([[1], [2]]);
  });

  it("does not request older pages when the current version is already acknowledged", async () => {
    const load = vi.fn().mockResolvedValue(page(1, ["2026.12.3", "2026.12.2"]));

    const result = await loadUnseenReleases("2026.12.3", load);

    expect(result.releases).toEqual([]);
    expect(load).toHaveBeenCalledOnce();
  });

  it("stops if a deployment changes the history while pages are being loaded", async () => {
    const load = vi.fn().mockResolvedValueOnce(page(1, ["2026.12.3", "2026.12.2"]))
      .mockResolvedValueOnce({ ...page(2, ["2026.12.1", "2026.11.2"]), currentVersion: "2026.12.4" });

    await expect(loadUnseenReleases(null, load)).rejects.toThrow();
  });
});
