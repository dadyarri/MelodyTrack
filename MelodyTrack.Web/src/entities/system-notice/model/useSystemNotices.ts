import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import type { Ulid } from "@/shared/api";

import { systemNoticeQueryKeys } from "../api/queryKeys";
import { systemNoticeApi } from "../api/systemNoticeApi";

export function useSystemNotices(preAuth = false) {
  const queryClient = useQueryClient();
  const query = useQuery({
    queryKey: preAuth ? systemNoticeQueryKeys.preAuth() : systemNoticeQueryKeys.authenticated(),
    queryFn: ({ signal }) => (preAuth ? systemNoticeApi.listPreAuth(signal) : systemNoticeApi.list(signal)),
    refetchInterval: 60_000,
  });
  const dismissMutation = useMutation({
    mutationFn: (id: Ulid) => systemNoticeApi.dismiss(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: systemNoticeQueryKeys.authenticated() });
    },
  });

  return {
    notices: query.data ?? [],
    dismiss: dismissMutation.mutate,
    dismissing: dismissMutation.isPending,
  };
}
