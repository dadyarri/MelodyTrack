import { Button, Space, Typography } from "antd";
import { useEffect } from "react";
import { isRouteErrorResponse, useRouteError } from "react-router";

import { clearChunkRetryMarker, isRecoverableChunkLoadError } from "@/shared/lib";
import { StatusBanner } from "@/shared/ui";

export function RouteErrorBoundary() {
  const error = useRouteError();
  const isChunkError = isRecoverableChunkLoadError(error);

  useEffect(() => {
    if (!isChunkError) {
      clearChunkRetryMarker();
    }
  }, [isChunkError]);

  return (
    <Space orientation="vertical" size={16} className="route-error-shell">
      <Typography.Title level={2} className="route-error-title">
        {isChunkError ? "Приложение обновилось" : "Не удалось открыть страницу"}
      </Typography.Title>
      <StatusBanner
        type="error"
        title={isChunkError ? "Файлы страницы устарели после обновления приложения." : "Во время навигации произошла ошибка."}
        description={getErrorDescription(error, isChunkError)}
      />
      <Space wrap>
        <Button
          type="primary"
          onClick={() => {
            clearChunkRetryMarker();
            window.location.reload();
          }}
        >
          Обновить приложение
        </Button>
        <Button
          onClick={() => {
            window.history.back();
          }}
        >
          Назад
        </Button>
      </Space>
    </Space>
  );
}

function getErrorDescription(error: unknown, isChunkError: boolean) {
  if (isChunkError) {
    return "Попробуйте обновить страницу. Если проблема повторяется, проверьте, не открыта ли старая вкладка после обновления.";
  }

  if (isRouteErrorResponse(error)) {
    return `${String(error.status)} ${error.statusText}`;
  }

  if (error instanceof Error && error.message) {
    return error.message;
  }

  return "Попробуйте повторить переход или обновить страницу.";
}
