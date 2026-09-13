import { RpgIcon } from "@/shared/ui/rpg";

import styles from "./PortalPlayerIdentity.module.css";

export function PortalPlayerIdentity({ alias }: { alias: string }) {
  return (
    <div className={styles.identity}>
      <RpgIcon name="flower" />
      <div><p className={styles.label}>Добро пожаловать,</p><p className={styles.alias}>{alias}</p></div>
    </div>
  );
}
