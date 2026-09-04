import type { ButtonProps } from "antd";
import { Button } from "antd";
import type { ReactNode } from "react";

import { Shortcut } from "./Shortcut";
import styles from "./ShortcutButton.module.css";

type ShortcutButtonProps = Omit<ButtonProps, "children"> & {
  shortcut: string;
  label: ReactNode;
  leadingIcon?: ReactNode;
};

export function ShortcutButton({ shortcut, label, leadingIcon, className, ...props }: ShortcutButtonProps) {
  return (
    <Button {...props} className={[styles.button, className].filter(Boolean).join(" ")} icon={leadingIcon}>
      <span className={styles.buttonLabel}>{label}</span>
      <Shortcut keyb={shortcut} />
    </Button>
  );
}
