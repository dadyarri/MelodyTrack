import { Alert, Button, Space, Spin, Typography } from "antd";

import { ApiErrorDetails } from "./ApiErrorDetails";

export type ListQueryStatusProps = {
  error?: unknown;
  isFetching?: boolean;
  onRetry?: () => void;
};

export function ListQueryStatus({ error, isFetching = false, onRetry }: ListQueryStatusProps) {
  if (error) {
    return (
      <Alert
        type="error"
        showIcon
        title="Не удалось обновить данные."
        description={<ApiErrorDetails error={error} />}
        action={
          onRetry ? (
            <Button size="small" onClick={onRetry}>
              Повторить
            </Button>
          ) : null
        }
      />
    );
  }

  if (isFetching) {
    return (
      <Space role="status" aria-live="polite">
        <Spin size="small" />
        <Typography.Text type="secondary">Обновляем данные…</Typography.Text>
      </Space>
    );
  }

  return null;
}
