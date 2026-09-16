import { Drawer } from "antd";
import { type ReactNode, useState } from "react";

import { PortalPlayerIdentity } from "@/entities/player-alias";
import { usePortalAppearance } from "@/shared/config";
import { MenuOutlined } from "@/shared/ui/icons";
import { PortalAppearanceSwitch, RpgButton, RpgSignature } from "@/shared/ui/rpg";

import styles from "./PortalHeader.module.css";

export function PortalHeader({ alias, actions, navigation }: { alias: string; actions?: ReactNode; navigation: ReactNode }) {
  const [mobileNavigationOpen, setMobileNavigationOpen] = useState(false);
  const { appearance } = usePortalAppearance();

  return (
    <header className={styles.header}>
      <a className={styles.skip} href="#portal-content">
        Перейти к содержимому
      </a>
      <div className={styles.top}>
        <div className={styles.brandGroup}>
          <RpgSignature compact />
          <div className={styles.brand}>
            <span>MelodyTrack</span>
          </div>
        </div>
        <PortalPlayerIdentity alias={alias} />
        <div className={styles.actions}>
          <RpgButton
            className={styles.mobileNavigationButton}
            aria-label="Открыть навигацию"
            title="Навигация"
            onClick={() => {
              setMobileNavigationOpen(true);
            }}
          >
            <MenuOutlined />
          </RpgButton>
          <PortalAppearanceSwitch />
          {actions}
        </div>
      </div>
      <div className={styles.desktopNavigation}>{navigation}</div>
      <Drawer
        rootClassName={styles.mobileNavigationDrawer}
        title="Навигация"
        placement="left"
        open={mobileNavigationOpen}
        onClose={() => {
          setMobileNavigationOpen(false);
        }}
        onClick={() => {
          setMobileNavigationOpen(false);
        }}
      >
        <div className={styles.mobileNavigationContent} data-portal-appearance={appearance}>
          {navigation}
        </div>
      </Drawer>
    </header>
  );
}
