import { formatShortcutLabel } from "@/shared/lib";

import styles from "./Shortcut.module.css";

export function Shortcut({ keyb }: { keyb: string }) {
  return <span className={styles.keycap}>{formatShortcutLabel(keyb)}</span>;
}
