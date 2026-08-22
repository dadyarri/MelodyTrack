import { useQueryClient } from "@tanstack/react-query";
import { Alert } from "antd";
import { useEffect, useRef, useState } from "react";

import { normalizeAppError } from "@/shared/api";

import { ApiErrorDetails } from "./ApiErrorDetails";

export function ApiErrorNotifier() {
  const queryClient = useQueryClient();
  const [error, setError] = useState<unknown>(null);
  const shownAtRef = useRef(new WeakMap<object, number>());

  useEffect(() => {
    const unsubscribeQuery = queryClient.getQueryCache().subscribe((event) => {
      if (event.type !== "updated" || event.query.state.status !== "error") {
        return;
      }
      if (event.query.meta?.suppressErrorNotification === true) {
        return;
      }

      publishError(event.query, event.query.state.errorUpdatedAt, event.query.state.error);
    });
    const unsubscribeMutation = queryClient.getMutationCache().subscribe((event) => {
      if (event.type !== "updated" || event.mutation.state.status !== "error") {
        return;
      }
      if (event.mutation.meta?.suppressErrorNotification === true) {
        return;
      }

      publishError(event.mutation, event.mutation.state.submittedAt, event.mutation.state.error);
    });

    return () => {
      unsubscribeQuery();
      unsubscribeMutation();
    };

    function publishError(owner: object, updatedAt: number, nextError: unknown) {
      if (shownAtRef.current.get(owner) === updatedAt) {
        return;
      }
      shownAtRef.current.set(owner, updatedAt);
      if (normalizeAppError(nextError).kind !== "canceled") {
        setError(nextError);
      }
    }
  }, [queryClient]);

  if (!error) {
    return null;
  }

  return (
    <section
      aria-label="Ошибка приложения"
      style={{ position: "fixed", zIndex: 1100, top: 12, right: 12, left: 12, maxWidth: 760, marginInline: "auto" }}
    >
      <Alert
        type="error"
        showIcon
        closable={{
          onClose: () => {
            setError(null);
          },
        }}
        title="Не удалось выполнить запрос"
        description={<ApiErrorDetails error={error} />}
      />
    </section>
  );
}
