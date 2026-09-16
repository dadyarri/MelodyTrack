import { type ReactNode, useEffect, useId, useRef } from "react";

import { usePortalAppearance } from "@/shared/config";

import { RpgButton } from "./RpgPrimitives";
import styles from "./RpgTheme.module.css";

export function RpgDialog({
  open,
  title,
  children,
  footer,
  pending = false,
  onRequestClose,
}: {
  open: boolean;
  title: string;
  children: ReactNode;
  footer?: ReactNode;
  pending?: boolean;
  onRequestClose: () => void;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const titleId = useId();

  const keepFocusInside = (event: React.KeyboardEvent<HTMLDialogElement>) => {
    if (event.key !== "Tab") return;
    const focusable = event.currentTarget.querySelectorAll<HTMLElement>(
      'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
    );
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  };
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
      onKeyDown={keepFocusInside}
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
