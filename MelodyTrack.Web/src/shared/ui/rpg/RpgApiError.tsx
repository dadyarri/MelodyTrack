import { useState } from "react";

import { getApiErrorMessage, normalizeAppError } from "@/shared/api";
import { copyTextToClipboard } from "@/shared/lib";

import { RpgButton, RpgState } from "./RpgPrimitives";
import styles from "./RpgTheme.module.css";

export function RpgApiError({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const appError = normalizeAppError(error);
  return (
    <RpgState kind="error" title="Не удалось выполнить запрос" action={onRetry ? <RpgButton onClick={onRetry}>Попробовать снова</RpgButton> : undefined}>
      <p>{getApiErrorMessage(appError)}</p>
      {appError.traceId ? <TraceDetails key={appError.traceId} traceId={appError.traceId} /> : null}
    </RpgState>
  );
}

function TraceDetails({ traceId }: { traceId: string }) {
  const [feedback, setFeedback] = useState("");
  return (
    <div className={styles.trace}>
      <span>Trace ID: <code>{traceId}</code></span>
      <RpgButton variant="quiet" onClick={() => {
        void copyTextToClipboard(traceId).then((copied) => {
          setFeedback(copied ? "Trace ID скопирован" : "Не удалось скопировать. Выделите Trace ID вручную.");
        });
      }}>Скопировать Trace ID</RpgButton>
      <span role="status">{feedback}</span>
    </div>
  );
}
