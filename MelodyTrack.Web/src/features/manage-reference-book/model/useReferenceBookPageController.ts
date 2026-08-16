import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { App as AntdApp } from "antd";
import { useState } from "react";

import type { ReferenceBookItem } from "@/entities/reference-book";
import type { Ulid } from "@/shared/api";
import { getApiErrorMessages } from "@/shared/api";
import { handleStaleEntityConflict } from "@/shared/lib";

type ReferenceBookControllerOptions<TItem> = {
  successMessages: {
    create: string;
    delete: string;
  };
  createItem: (values: { name: string }) => Promise<unknown>;
  deleteItem: (id: Ulid, options?: { expectedActivityId?: Ulid }) => Promise<unknown>;
  listQueryKey: readonly unknown[];
  listQueryFn: () => Promise<TItem[]>;
  invalidateQueryKeys?: ReadonlyArray<readonly unknown[]>;
  staleConflict: {
    title: string;
    okText: string;
    cancelText: string;
  };
};

export function useReferenceBookPageController<TItem extends ReferenceBookItem>({
  successMessages,
  createItem,
  deleteItem,
  listQueryKey,
  listQueryFn,
  invalidateQueryKeys = [],
  staleConflict,
}: ReferenceBookControllerOptions<TItem>) {
  const [isCreateOpen, setCreateOpen] = useState(false);
  const queryClient = useQueryClient();
  const { message, modal } = AntdApp.useApp();
  const showErrors = (error: unknown) => {
    for (const errorMessage of getApiErrorMessages(error)) {
      void message.error(errorMessage);
    }
  };

  const query = useQuery({
    queryKey: listQueryKey,
    queryFn: listQueryFn,
  });

  const createMutation = useMutation({
    mutationFn: createItem,
    onSuccess: async () => {
      message.success(successMessages.create);
      setCreateOpen(false);
      await queryClient.invalidateQueries({ queryKey: listQueryKey });
    },
    onError: showErrors,
  });

  const deleteMutation = useMutation({
    mutationFn: ({ id, expectedActivityId }: { id: Ulid; expectedActivityId?: Ulid }) => deleteItem(id, { expectedActivityId }),
    onSuccess: async () => {
      message.success(successMessages.delete);
      await queryClient.invalidateQueries({ queryKey: listQueryKey });
      await Promise.all(invalidateQueryKeys.map((queryKey) => queryClient.invalidateQueries({ queryKey })));
    },
    onError: async (error, variables) => {
      await handleStaleEntityConflict({
        error,
        modal,
        queryClient,
        invalidateQueryKey: listQueryKey,
        showErrors,
        title: staleConflict.title,
        okText: staleConflict.okText,
        cancelText: staleConflict.cancelText,
        onConfirm: (conflict) => {
          deleteMutation.mutate({ id: variables.id, expectedActivityId: conflict.currentActivity?.id });
        },
        onReload: () => {
          void queryClient.invalidateQueries({ queryKey: listQueryKey });
        },
      });
    },
  });

  return {
    isCreateOpen,
    setCreateOpen,
    query,
    createMutation,
    deleteMutation,
    modal,
    onCreate: (values: { name: string }) => {
      createMutation.mutate(values);
    },
    onDelete: (id: Ulid, expectedActivityId?: Ulid) => {
      deleteMutation.mutate({ id, expectedActivityId });
    },
  };
}
