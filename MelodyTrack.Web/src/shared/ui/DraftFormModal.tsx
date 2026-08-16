import { Alert, Button, Modal, type ModalProps, Space } from "antd";
import type { ReactNode } from "react";

import type { DurableFormStatus } from "@/shared/lib/react";

import { DraftModalFooter } from "./DraftModalFooter";
import { DraftModalTitle } from "./DraftModalTitle";

type DraftFormModalProps = Omit<ModalProps, "title" | "footer"> & {
  title: string;
  restored: boolean;
  saveStatus?: DurableFormStatus;
  onClearDraft: () => void;
  showClearDraft?: boolean;
  showDraftState?: boolean;
  stale?: boolean;
  onReapplyDraft?: () => void;
  onRetryDraft?: () => void;
  children: ReactNode;
};

export function DraftFormModal({
  title,
  restored,
  saveStatus,
  onClearDraft,
  showClearDraft = true,
  showDraftState = true,
  stale = false,
  onReapplyDraft,
  onRetryDraft,
  children,
  confirmLoading = false,
  okButtonProps,
  cancelButtonProps,
  cancelText,
  onCancel,
  onOk,
  ...props
}: DraftFormModalProps) {
  return (
    <Modal
      {...props}
      confirmLoading={confirmLoading}
      okButtonProps={{ ...okButtonProps, disabled: confirmLoading || okButtonProps?.disabled }}
      cancelButtonProps={cancelButtonProps}
      cancelText={cancelText}
      onCancel={onCancel}
      onOk={(event) => {
        if (!confirmLoading) {
          onOk?.(event);
        }
      }}
      title={showDraftState ? <DraftModalTitle title={title} restored={restored} saveStatus={saveStatus} onRetry={onRetryDraft} /> : title}
      footer={(_, { CancelBtn, OkBtn }) => (
        <DraftModalFooter
          onClearDraft={onClearDraft}
          showClearDraft={showClearDraft}
          CancelBtn={CancelBtn}
          OkBtn={OkBtn}
          cancelText={cancelText}
          cancelButtonProps={cancelButtonProps}
          keepCancelAvailable={confirmLoading}
          onCancel={onCancel}
        />
      )}
    >
      <div
        aria-busy={confirmLoading}
        onSubmitCapture={(event) => {
          if (confirmLoading) {
            event.preventDefault();
            event.stopPropagation();
          }
        }}
      >
        <span className="visually-hidden" role="status" aria-live="polite">
          {confirmLoading ? "Сохраняем изменения…" : ""}
        </span>
        {stale ? (
          <Alert
            type="warning"
            showIcon
            title="Черновик основан на более старой версии"
            description="Данные на сервере изменились. Черновик не применён автоматически."
            action={
              <Space>
                {onReapplyDraft ? <Button onClick={onReapplyDraft}>Применить черновик</Button> : null}
                <Button onClick={onClearDraft}>Отбросить</Button>
              </Space>
            }
          />
        ) : null}
        {children}
      </div>
    </Modal>
  );
}
