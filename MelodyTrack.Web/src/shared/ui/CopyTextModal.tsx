import { Button, Input, Modal, Typography } from "antd";
import type { ReactNode } from "react";
import { useCallback, useId, useState } from "react";

import { copyTextToClipboard } from "@/shared/lib";

import styles from "./CopyTextModal.module.css";

export type CopyTextModalContent = {
  value: string;
  title?: ReactNode;
  description?: ReactNode;
  fieldLabel?: ReactNode;
  copyButtonLabel?: string;
  copiedConfirmation?: string;
  copyFailure?: string;
  closeLabel?: string;
  warning?: ReactNode;
  context?: ReactNode;
  followUpAction?: {
    label: string;
    href: string;
    target?: "_blank" | "_self";
  };
};

export type CopyTextModalProps = {
  open: boolean;
  content: CopyTextModalContent | null;
  onClose: () => void;
};

type CopyFeedback = { value: string; kind: "success" | "error"; message: string } | null;

const defaults = {
  title: "Текст готов",
  description: "Проверьте текст и скопируйте его.",
  fieldLabel: "Текст",
  copyButtonLabel: "Скопировать",
  copiedConfirmation: "Текст скопирован",
  closeLabel: "Закрыть",
  copyFailure: "Не удалось скопировать автоматически. Выделите текст и скопируйте его вручную.",
};

export function CopyTextModal({ open, content, onClose }: CopyTextModalProps) {
  const [feedback, setFeedback] = useState<CopyFeedback>(null);
  const fieldId = useId();
  const focusCopyButton = useCallback((button: HTMLButtonElement | null) => {
    button?.focus();
  }, []);

  if (!content) {
    return null;
  }

  const value = content.value;
  const currentFeedback = feedback?.value === value ? feedback : null;

  const copyText = () => {
    void copyTextToClipboard(value).then((copied) => {
      if (copied) {
        setFeedback({ value, kind: "success", message: content.copiedConfirmation ?? defaults.copiedConfirmation });
        return;
      }

      setFeedback({ value, kind: "error", message: content.copyFailure ?? defaults.copyFailure });
    });
  };

  const close = () => {
    setFeedback(null);
    onClose();
  };

  return (
    <Modal
      className={styles.modal}
      open={open && Boolean(content)}
      title={content.title ?? defaults.title}
      width={560}
      footer={
        <div className={styles.footer}>
          <Button onClick={close}>{content.closeLabel ?? defaults.closeLabel}</Button>
          <Button ref={focusCopyButton} type="primary" className={styles.copyButton} onClick={copyText}>
            {content.copyButtonLabel ?? defaults.copyButtonLabel}
          </Button>
          {currentFeedback?.kind === "success" && content.followUpAction ? (
            <Button
              className={styles.followUpButton}
              href={content.followUpAction.href}
              target={content.followUpAction.target}
              rel={content.followUpAction.target === "_blank" ? "noopener noreferrer" : undefined}
            >
              {content.followUpAction.label}
            </Button>
          ) : null}
        </div>
      }
      onCancel={close}
      destroyOnHidden
    >
      <div className={styles.content}>
        {content.description ?? defaults.description}
        {content.context ? <div>{content.context}</div> : null}
        {content.warning ? <div className={styles.warning}>{content.warning}</div> : null}
        <div className={styles.field}>
          <Typography.Text strong>
            <label htmlFor={fieldId}>{content.fieldLabel ?? defaults.fieldLabel}</label>
          </Typography.Text>
          <Input.TextArea
            id={fieldId}
            className={styles.valueInput}
            readOnly
            value={value}
            autoSize={{ minRows: 2, maxRows: 5 }}
            onFocus={(event) => {
              event.currentTarget.select();
            }}
          />
        </div>
        <Typography.Text
          className={styles.feedback}
          type={currentFeedback?.kind === "error" ? "danger" : currentFeedback?.kind === "success" ? "success" : undefined}
          role="status"
          aria-live="polite"
        >
          {currentFeedback?.message ?? ""}
        </Typography.Text>
      </div>
    </Modal>
  );
}
