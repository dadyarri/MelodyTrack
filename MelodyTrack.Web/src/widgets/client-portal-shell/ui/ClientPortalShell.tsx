import type { ReactNode } from "react";
import { Outlet } from "react-router";

import { SystemNoticeCenter } from "@/entities/system-notice";
import { PortalControlsThemeProvider } from "@/shared/config";
import { RpgFrame, RpgTheme } from "@/shared/ui/rpg";

import styles from "./ClientPortalShell.module.css";

export function ClientPortalShell({ header }: { header: ReactNode }) {
  return (
    <RpgTheme className={styles.shell}>
      <PortalControlsThemeProvider>
        <RpgFrame className={styles.inner}>
          {header}
          <SystemNoticeCenter />
          <main id="portal-content" tabIndex={-1} className={styles.content}>
            <Outlet />
          </main>
        </RpgFrame>
      </PortalControlsThemeProvider>
    </RpgTheme>
  );
}
