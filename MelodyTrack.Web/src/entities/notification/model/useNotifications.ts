import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import type { Ulid } from "@/shared/api";

import { notificationApi } from "../api/notificationApi";
import { notificationQueryKeys } from "../api/queryKeys";

export function useNotifications() {
  const queryClient = useQueryClient();
  const query = useQuery({
    queryKey: notificationQueryKeys.list(),
    queryFn: ({ signal }) => notificationApi.list(signal),
    refetchInterval: 30_000,
  });
  const markReadMutation = useMutation({
    mutationFn: (id: Ulid) => notificationApi.markRead(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: notificationQueryKeys.list() });
    },
  });
  const markAllReadMutation = useMutation({
    mutationFn: () => notificationApi.markAllRead(),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: notificationQueryKeys.list() });
    },
  });

  return {
    items: query.data?.items ?? [],
    unreadCount: query.data?.unreadCount ?? 0,
    loading: query.isLoading,
    refetch: query.refetch,
    markRead: markReadMutation.mutate,
    markAllRead: markAllReadMutation.mutate,
    markingAllRead: markAllReadMutation.isPending,
  };
}
