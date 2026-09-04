import { useQuery, useQueryClient } from "@tanstack/react-query";

import { getReleaseHistory, type ReleaseEntry, type ReleaseHistory, releaseQueryKeys } from "@/entities/release";

const releaseHistoryStaleTime = 5 * 60 * 1000;

export type UnseenReleaseHistory = {
  currentVersion: string;
  releases: ReleaseEntry[];
};

export function useUnseenReleaseHistory(seenVersion: string | null, enabled: boolean) {
  const queryClient = useQueryClient();

  return useQuery({
    queryKey: [...releaseQueryKeys.all, "unseen", seenVersion] as const,
    queryFn: () =>
      loadUnseenReleaseHistory(seenVersion, (page) =>
        queryClient.fetchQuery({
          queryKey: releaseQueryKeys.history(page),
          queryFn: () => getReleaseHistory(page),
          staleTime: releaseHistoryStaleTime,
        }),
      ),
    enabled,
    staleTime: releaseHistoryStaleTime,
    retry: 1,
    meta: { suppressErrorNotification: true },
  });
}

export async function loadUnseenReleaseHistory(
  seenVersion: string | null,
  loadPage: (page: number) => Promise<ReleaseHistory> = getReleaseHistory,
): Promise<UnseenReleaseHistory> {
  const firstPage = await loadPage(1);
  return collectUnseenReleases(firstPage, seenVersion, loadPage, []);
}

async function collectUnseenReleases(
  history: ReleaseHistory,
  seenVersion: string | null,
  loadPage: (page: number) => Promise<ReleaseHistory>,
  releases: ReleaseEntry[],
): Promise<UnseenReleaseHistory> {
  const seenIndex = seenVersion ? history.releases.findIndex((release) => release.version === seenVersion) : -1;
  releases.push(...(seenIndex >= 0 ? history.releases.slice(0, seenIndex) : history.releases));

  if (seenIndex >= 0 || !history.hasNextPage) {
    return { currentVersion: history.currentVersion, releases };
  }

  const nextPage = await loadPage(history.page + 1);
  return collectUnseenReleases(nextPage, seenVersion, loadPage, releases);
}
