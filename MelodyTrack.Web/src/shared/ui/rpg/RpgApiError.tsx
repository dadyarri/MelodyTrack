import { getApiErrorMessage, normalizeAppError } from "@/shared/api";
import { useCopyTraceId } from "@/shared/ui";

import { RpgButton, RpgState } from "./RpgPrimitives";
import styles from "./RpgTheme.module.css";

export function RpgApiError({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const appError = normalizeAppError(error);
  return (
    <RpgState
      kind="error"
      title="Не удалось выполнить запрос"
      action={onRetry ? <RpgButton onClick={onRetry}>Попробовать снова</RpgButton> : undefined}
    >
      <p>{getApiErrorMessage(appError)}</p>
      {appError.traceId ? <TraceDetails key={appError.traceId} traceId={appError.traceId} /> : null}
    </RpgState>
  );
}

function TraceDetails({ traceId }: { traceId: string }) {
  const { copyTraceId, feedback } = useCopyTraceId(traceId);
  return (
    <div className={styles.trace}>
      <span>
        Trace ID: <code>{traceId}</code>
      </span>
      <RpgButton variant="quiet" onClick={copyTraceId}>
        Скопировать Trace ID
      </RpgButton>
      <span role="status">{feedback}</span>
    </div>
  );
}
