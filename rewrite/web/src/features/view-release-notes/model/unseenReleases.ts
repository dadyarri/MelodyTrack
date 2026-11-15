import type { ReleaseEntry, ReleaseHistory } from "@/entities/app-release";
import { invalidResponse } from "@/shared/api";

export async function loadUnseenReleases(
  seenVersion: string | null,
  loadPage: (page: number) => Promise<ReleaseHistory>,
): Promise<{ currentVersion: string; releases: ReleaseEntry[] }> {
  const releases: ReleaseEntry[] = [];
  let currentVersion: string | null = null;
  let totalPages: number | null = null;
  for (let page = 1; ; page += 1) {
    const history = await loadPage(page);
    currentVersion ??= history.currentVersion;
    totalPages ??= history.totalPages;
    if (
      history.page !== page ||
      history.currentVersion !== currentVersion ||
      history.totalPages !== totalPages ||
      (history.hasNextPage && history.releases.length === 0)
    )
      throw invalidResponse();
    const seenIndex = seenVersion
      ? history.releases.findIndex((entry) => entry.version === seenVersion)
      : -1;
    releases.push(...(seenIndex < 0 ? history.releases : history.releases.slice(0, seenIndex)));
    if (seenIndex >= 0 || !history.hasNextPage) return { currentVersion, releases };
  }
}
