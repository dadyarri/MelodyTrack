import { Button, Modal, Typography } from "antd";
import { useEffect, useMemo, useState } from "react";

import { downloadBlob } from "@/shared/lib";

import type { PreparedScheduleShare } from "../model/useTasksPageController";
import styles from "./PreparedScheduleShareModal.module.css";

type CopyFeedback = { blob: Blob; kind: "success" | "error"; message: string } | null;

export function PreparedScheduleShareModal({ content, onClose }: { content: PreparedScheduleShare | null; onClose: () => void }) {
  const [feedback, setFeedback] = useState<CopyFeedback>(null);
  const previewUrl = useMemo(() => (content ? URL.createObjectURL(content.blob) : null), [content]);

  useEffect(() => {
    return () => {
      if (previewUrl) {
        URL.revokeObjectURL(previewUrl);
      }
    };
  }, [previewUrl]);

  if (!content) {
    return null;
  }

  const currentFeedback = feedback?.blob === content.blob ? feedback : null;
  const isTelegram = content.messengerUrl.startsWith("tg://");

  const copyImage = () => {
    const clipboardItem = Reflect.get(globalThis, "ClipboardItem") as typeof ClipboardItem | undefined;
    const clipboard = Reflect.get(navigator, "clipboard") as Clipboard | undefined;
    if (!clipboardItem || !clipboard || typeof clipboard.write !== "function") {
      setFeedback({
        blob: content.blob,
        kind: "error",
        message: "Браузер не поддерживает копирование изображения. Скачайте PNG и прикрепите его вручную.",
      });
      return;
    }

    let copyAttempt: Promise<void>;
    try {
      copyAttempt = clipboard.write([
        new clipboardItem({
          [content.blob.type]: content.blob,
        }),
      ]);
    } catch {
      setFeedback({
        blob: content.blob,
        kind: "error",
        message: "Не удалось скопировать изображение. Скачайте PNG и прикрепите его вручную.",
      });
      return;
    }

    void copyAttempt.then(
      () => {
        setFeedback({
          blob: content.blob,
          kind: "success",
          message: "Расписание скопировано. Теперь можно открыть мессенджер.",
        });
      },
      () => {
        setFeedback({
          blob: content.blob,
          kind: "error",
          message: "Не удалось скопировать изображение. Скачайте PNG и прикрепите его вручную.",
        });
      },
    );
  };

  const close = () => {
    setFeedback(null);
    onClose();
  };

  return (
    <Modal
      open
      title="Расписание готово"
      width={620}
      onCancel={close}
      destroyOnHidden
      footer={
        <div className={styles.footer}>
          <Button onClick={close}>Закрыть</Button>
          <Button
            onClick={() => {
              downloadBlob(content.blob, content.fileName);
            }}
          >
            Скачать PNG
          </Button>
          <Button type="primary" onClick={copyImage}>
            Скопировать изображение
          </Button>
          {currentFeedback?.kind === "success" ? (
            <Button
              href={content.messengerUrl}
              target={isTelegram ? undefined : "_blank"}
              rel={isTelegram ? undefined : "noopener noreferrer"}
            >
              {isTelegram ? "Открыть Telegram" : "Открыть VK"}
            </Button>
          ) : null}
        </div>
      }
    >
      <div className={styles.content}>
        <Typography.Text>Скопируйте изображение, затем откройте мессенджер и вставьте его в сообщение.</Typography.Text>
        {previewUrl ? <img className={styles.preview} src={previewUrl} alt="Подготовленное расписание преподавателя" /> : null}
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
