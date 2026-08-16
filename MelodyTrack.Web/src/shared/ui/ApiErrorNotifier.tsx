import { useQueryClient } from "@tanstack/react-query";
import { App as AntdApp } from "antd";
import { useEffect, useRef } from "react";

import { getApiErrorMessages } from "@/shared/api";

export function ApiErrorNotifier() {
  const queryClient = useQueryClient();
  const { message } = AntdApp.useApp();
  const shownAtRef = useRef(new WeakMap<object, number>());

  useEffect(() => {
    return queryClient.getQueryCache().subscribe((event) => {
      if (event.type !== "updated" || event.query.state.status !== "error") {
        return;
      }

      if (event.query.meta?.suppressErrorNotification === true) {
        return;
      }

      const errorUpdatedAt = event.query.state.errorUpdatedAt;
      if (shownAtRef.current.get(event.query) === errorUpdatedAt) {
        return;
      }

      shownAtRef.current.set(event.query, errorUpdatedAt);
      for (const errorMessage of getApiErrorMessages(event.query.state.error)) {
        message.error(errorMessage);
      }
    });
  }, [message, queryClient]);

  return null;
}
