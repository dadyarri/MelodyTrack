import { Button } from "antd";
import type { ComponentProps, ComponentType, MouseEvent, ReactNode } from "react";

type DraftModalFooterProps = {
  onClearDraft: () => void;
  showClearDraft?: boolean;
  CancelBtn: ComponentType;
  OkBtn: ComponentType;
  cancelText?: ReactNode;
  cancelButtonProps?: ComponentProps<typeof Button>;
  keepCancelAvailable?: boolean;
  onCancel?: (event: MouseEvent<HTMLButtonElement>) => void;
};

export function DraftModalFooter({
  onClearDraft,
  showClearDraft = true,
  CancelBtn,
  OkBtn,
  cancelText,
  cancelButtonProps,
  keepCancelAvailable = false,
  onCancel,
}: DraftModalFooterProps) {
  return (
    <>
      {showClearDraft ? <Button onClick={onClearDraft}>Отбросить черновик</Button> : null}
      {keepCancelAvailable ? (
        <Button {...cancelButtonProps} disabled={cancelButtonProps?.disabled} onClick={onCancel}>
          {cancelText ?? "Отмена"}
        </Button>
      ) : (
        <CancelBtn />
      )}
      <OkBtn />
    </>
  );
}
