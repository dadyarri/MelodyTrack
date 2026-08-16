import { Alert, Button, Space, Spin, Typography } from "antd";

export type ListQueryStatusProps = {
  isError?: boolean;
  isFetching?: boolean;
  onRetry?: () => void;
};

export function ListQueryStatus({ isError = false, isFetching = false, onRetry }: ListQueryStatusProps) {
  if (isError) {
    return (
      <Alert
        type="error"
        showIcon
        title="Не удалось обновить данные."
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
