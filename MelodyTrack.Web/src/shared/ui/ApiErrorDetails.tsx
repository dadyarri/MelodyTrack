import { Space, Typography } from "antd";

import { getApiErrorMessage, normalizeAppError } from "@/shared/api";

import { CopyTraceIdButton } from "./CopyTraceIdButton";

type ApiErrorDetailsProps = {
  error: unknown;
  fallbackMessage?: string;
};

export function ApiErrorDetails({ error, fallbackMessage }: ApiErrorDetailsProps) {
  const appError = normalizeAppError(error);
  const message = getApiErrorMessage(appError) || fallbackMessage || "Не удалось выполнить запрос.";

  return (
    <Space orientation="vertical" size={4}>
      <Typography.Text style={{ whiteSpace: "pre-wrap" }}>{message}</Typography.Text>
      {appError.traceId ? <CopyTraceIdButton traceId={appError.traceId} /> : null}
    </Space>
  );
}
