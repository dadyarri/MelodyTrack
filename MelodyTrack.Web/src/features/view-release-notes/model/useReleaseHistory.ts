import { useQuery } from "@tanstack/react-query";

import { getReleaseHistory, releaseQueryKeys } from "@/entities/release";

export function useReleaseHistory(page = 1) {
  return useQuery({
    queryKey: releaseQueryKeys.history(page),
    queryFn: () => getReleaseHistory(page),
    staleTime: 5 * 60 * 1000,
    retry: 1,
    meta: { suppressErrorNotification: true },
  });
}
