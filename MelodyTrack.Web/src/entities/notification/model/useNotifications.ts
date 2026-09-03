import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import type { Ulid } from "@/shared/api";

import { notificationApi } from "../api/notificationApi";
import { notificationQueryKeys } from "../api/queryKeys";

export function useNotifications() {
  const queryClient = useQueryClient();
  const listQueryKey = notificationQueryKeys.list();
  const query = useQuery({
    queryKey: listQueryKey,
    queryFn: ({ signal }) => notificationApi.listUnread(signal),
    refetchInterval: 30_000,
  });
  const markReadMutation = useMutation({
    mutationFn: (id: Ulid) => notificationApi.markRead(id),
    onSuccess: (_, id) => {
      queryClient.setQueryData<Awaited<ReturnType<typeof notificationApi.listUnread>>>(listQueryKey, (current) => {
        if (!current) {
          return current;
        }

        const items = current.items.filter((item) => item.id !== id);
        return items.length === current.items.length ? current : { ...current, items, unreadCount: Math.max(0, current.unreadCount - 1) };
      });
      void queryClient.invalidateQueries({ queryKey: listQueryKey });
    },
  });
  const markAllReadMutation = useMutation({
    mutationFn: () => notificationApi.markAllRead(),
    onSuccess: () => {
      queryClient.setQueryData<Awaited<ReturnType<typeof notificationApi.listUnread>>>(listQueryKey, (current) =>
        current ? { ...current, items: [], unreadCount: 0 } : current,
      );
      void queryClient.invalidateQueries({ queryKey: listQueryKey });
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
