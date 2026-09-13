import { type ReactNode, useEffect, useId, useRef } from "react";

import { usePortalAppearance } from "@/shared/config";

import { RpgButton } from "./RpgPrimitives";
import styles from "./RpgTheme.module.css";

export function RpgDialog({ open, title, children, footer, pending = false, onRequestClose }: {
  open: boolean;
  title: string;
  children: ReactNode;
  footer?: ReactNode;
  pending?: boolean;
  onRequestClose: () => void;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  const { appearance } = usePortalAppearance();

  useEffect(() => {
    const dialog = ref.current;
    if (!dialog || !open) return;
    dialog.showModal();
    return () => {
      dialog.close();
    };
  }, [open]);

  return (
    <dialog
      ref={ref}
      className={`${styles.theme} ${styles.dialog}`}
      data-portal-appearance={appearance}
      aria-labelledby={titleId}
      aria-busy={pending}
      onCancel={(event) => {
        event.preventDefault();
        if (!pending) onRequestClose();
      }}
    >
      <div className={styles.dialogHeader}>
        <h2 id={titleId}>{title}</h2>
        <RpgButton variant="quiet" disabled={pending} onClick={onRequestClose}>
          Закрыть
        </RpgButton>
      </div>
      <div className={styles.dialogBody}>{children}</div>
      {footer ? <div className={styles.dialogFooter}>{footer}</div> : null}
    </dialog>
  );
}
