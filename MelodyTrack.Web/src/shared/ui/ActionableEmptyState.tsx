import { Button, Empty } from "antd";

export function ActionableEmptyState({
  description,
  actionLabel,
  onAction,
}: {
  description: string;
  actionLabel?: string;
  onAction?: () => void;
}) {
  return (
    <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={description}>
      {actionLabel && onAction ? (
        <Button type="primary" onClick={onAction}>
          {actionLabel}
        </Button>
      ) : null}
    </Empty>
  );
}
