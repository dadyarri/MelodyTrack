import { Dialog } from "@base-ui/react/dialog";
import type { ReactNode } from "react";
import { Icon } from "../Icon";
import { PaperButton } from "./PaperButton";
import styles from "./PaperDrawer.module.css";

export function PaperDrawer({
  open,
  onClose,
  title,
  children,
  footer,
  className,
  "aria-label": label,
}: {
  open: boolean;
  onClose: () => void;
  title: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
  className?: string | undefined;
  "aria-label"?: string;
}) {
  return (
    <Dialog.Root
      open={open}
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
    >
      <Dialog.Portal>
        <Dialog.Backdrop className={styles.backdrop} />
        <Dialog.Popup
          className={[styles.drawer, className].filter(Boolean).join(" ")}
          aria-label={label}
        >
          <header className={styles.header}>
            <Dialog.Title className={styles.title}>{title}</Dialog.Title>
            <Dialog.Close
              render={<PaperButton aria-label="Закрыть" icon={<Icon name="close" />} />}
            />
          </header>
          <div className={styles.body}>{children}</div>
          {footer ? <footer className={styles.footer}>{footer}</footer> : null}
        </Dialog.Popup>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
