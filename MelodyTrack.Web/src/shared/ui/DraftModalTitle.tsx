import { Tooltip } from "antd";
import type { ReactNode } from "react";

import type { DurableFormStatus } from "@/shared/lib/react";
import { CloudDownloadOutlined, CloudOutlined } from "@/shared/ui/icons";

import styles from "./DraftModalTitle.module.css";

type DraftModalTitleProps = {
  title: ReactNode;
  restored: boolean;
  saveStatus?: DurableFormStatus;
  restoredLabel?: string;
  idleLabel?: string;
  onRetry?: () => void;
};

export function DraftModalTitle({
  title,
  restored,
  saveStatus = "saved",
  restoredLabel = "Черновик восстановлен",
  idleLabel = "Черновик сохранён",
  onRetry,
}: DraftModalTitleProps) {
  const label =
    saveStatus === "loading"
      ? "Загружаем черновик…"
      : saveStatus === "saving"
        ? "Сохраняем черновик…"
        : saveStatus === "failed"
          ? "Не удалось сохранить черновик"
          : saveStatus === "restored" || restored
            ? restoredLabel
            : idleLabel;

  const stateClassName = [
    styles.titleState,
    restored ? styles.titleStateRestored : null,
    saveStatus === "failed" ? styles.titleStateFailed : null,
  ]
    .filter(Boolean)
    .join(" ");
  const icon = restored ? <CloudDownloadOutlined /> : <CloudOutlined />;
  const tooltip = saveStatus === "failed" && onRetry ? `${label}. Нажмите, чтобы повторить.` : label;

  return (
    <span className={styles.title}>
      <span className={styles.titleText}>{title}</span>
      <Tooltip title={tooltip}>
        {saveStatus === "failed" && onRetry ? (
          <button type="button" className={stateClassName} aria-label={tooltip} onClick={onRetry}>
            {icon}
          </button>
        ) : (
          <span className={stateClassName} role="status" aria-label={label}>
            {icon}
          </span>
        )}
      </Tooltip>
    </span>
  );
}
