import { useEffect, useId, useLayoutEffect, useRef } from "react";
import { toast } from "sonner";

type ErrorNotificationOptions = {
  title?: string;
  retry?: (() => void) | undefined;
};

export function useErrorNotification(
  error: string | null | undefined,
  { title, retry }: ErrorNotificationOptions = {},
) {
  const id = useId();
  const retryAction = useRef(retry);
  const hasRetry = Boolean(retry);

  useLayoutEffect(() => {
    retryAction.current = retry;
  }, [retry]);

  useEffect(() => {
    if (!error) return;
    toast.error(title ?? error, {
      id,
      ...(title ? { description: error } : {}),
      duration: Number.POSITIVE_INFINITY,
      ...(hasRetry
        ? { action: { label: "Повторить", onClick: () => retryAction.current?.() } }
        : {}),
    });
    return () => {
      toast.dismiss(id);
    };
  }, [error, hasRetry, id, title]);
}
