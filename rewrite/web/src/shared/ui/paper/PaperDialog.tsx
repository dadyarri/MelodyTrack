import { Dialog } from "@base-ui/react/dialog";
import type { ReactNode } from "react";
import { Icon } from "../Icon";
import styles from "./PaperDialog.module.css";

export function PaperDialog({
  open,
  onClose,
  title,
  children,
  footer,
  className = "",
}: {
  open: boolean;
  onClose: () => void;
  title: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
  className?: string | undefined;
}) {
  return (
    <Dialog.Root open={open} onOpenChange={(next) => !next && onClose()}>
      <Dialog.Portal>
        <Dialog.Backdrop className={styles.backdrop} />
        <Dialog.Popup className={`${styles.popup} ${className}`}>
          <header className={styles.header}>
            <Dialog.Title className={styles.title}>{title}</Dialog.Title>
            <Dialog.Close aria-label="Закрыть окно" title="Закрыть окно" className={styles.close}>
              <Icon name="close" />
            </Dialog.Close>
          </header>
          <div className={styles.body}>{children}</div>
          {footer !== undefined && <footer className={styles.footer}>{footer}</footer>}
        </Dialog.Popup>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
