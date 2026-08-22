import { Empty, Typography } from "antd";

import { ApiErrorDetails } from "./ApiErrorDetails";
import { StatusBanner } from "./StatusBanner";

type QueryStateBlockProps = {
  isLoading?: boolean;
  error?: unknown;
  isEmpty?: boolean;
  loadingText?: string;
  emptyText?: string;
  errorMessage?: string;
};

export function QueryStateBlock({
  isLoading = false,
  error,
  isEmpty = false,
  loadingText = "Загрузка...",
  emptyText = "Нет данных",
  errorMessage = "Не удалось загрузить данные.",
}: QueryStateBlockProps) {
  if (isLoading) {
    return <Typography.Text type="secondary">{loadingText}</Typography.Text>;
  }

  if (error) {
    return (
      <StatusBanner type="error" title={errorMessage} description={<ApiErrorDetails error={error} fallbackMessage={errorMessage} />} />
    );
  }

  if (isEmpty) {
    return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={emptyText} />;
  }

  return null;
}
