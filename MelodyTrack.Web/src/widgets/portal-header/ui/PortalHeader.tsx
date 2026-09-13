import type { ReactNode } from "react";

import { PortalPlayerIdentity } from "@/entities/player-alias";
import { PortalAppearanceSwitch, RpgSignature } from "@/shared/ui/rpg";

import styles from "./PortalHeader.module.css";

export function PortalHeader({ alias, actions, navigation }: { alias: string; actions?: ReactNode; navigation: ReactNode }) {
  return (
    <header className={styles.header}>
      <a className={styles.skip} href="#portal-content">Перейти к содержимому</a>
      <div className={styles.top}>
        <div className={styles.brandGroup}>
          <RpgSignature />
          <div className={styles.brand}><span>MelodyTrack</span><span>Гильдия музыкантов</span></div>
        </div>
        <PortalPlayerIdentity alias={alias} />
        <div className={styles.actions}><PortalAppearanceSwitch />{actions}</div>
      </div>
      {navigation}
    </header>
  );
}
