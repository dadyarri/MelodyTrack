import { Button, Typography } from "antd";
import { useState } from "react";

import { copyTextToClipboard } from "@/shared/lib";

type CopyTraceIdButtonProps = {
  traceId: string;
};

export function CopyTraceIdButton({ traceId }: CopyTraceIdButtonProps) {
  const [feedback, setFeedback] = useState<string | null>(null);

  const copyTraceId = () => {
    void copyTextToClipboard(traceId).then((copied) => {
      setFeedback(copied ? "Trace ID скопирован" : "Не удалось скопировать. Выделите Trace ID вручную.");
    });
  };

  return (
    <span>
      <Typography.Text code>{traceId}</Typography.Text>{" "}
      <Button size="small" onClick={copyTraceId}>
        Скопировать Trace ID
      </Button>{" "}
      <Typography.Text type="secondary" role="status" aria-live="polite">
        {feedback}
      </Typography.Text>
    </span>
  );
}
