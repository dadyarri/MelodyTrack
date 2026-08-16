import { Empty, Typography } from "antd";

import { StatusBanner } from "./StatusBanner";

type QueryStateBlockProps = {
  isLoading?: boolean;
  isError?: boolean;
  isEmpty?: boolean;
  loadingText?: string;
  emptyText?: string;
  errorMessage?: string;
};

export function QueryStateBlock({
  isLoading = false,
  isError = false,
  isEmpty = false,
  loadingText = "Загрузка...",
  emptyText = "Нет данных",
  errorMessage = "Не удалось загрузить данные.",
}: QueryStateBlockProps) {
  if (isLoading) {
    return <Typography.Text type="secondary">{loadingText}</Typography.Text>;
  }

  if (isError) {
    return <StatusBanner type="error" title={errorMessage} />;
  }

  if (isEmpty) {
    return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={emptyText} />;
  }

  return null;
}
