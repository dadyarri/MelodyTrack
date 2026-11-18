import { Menu } from "@base-ui/react/menu";
import { useState } from "react";
import { NavLink, Outlet } from "react-router";
import { useStaffSession } from "@/entities/session";
import { useEmployeeTraining } from "@/features/employee-training";
import { ReleaseNotesProvider, ReleaseVersion } from "@/features/view-release-notes";
import type { Appearance } from "@/shared/lib/appearance";
import { Brand } from "@/shared/ui/Brand";
import { Icon } from "@/shared/ui/Icon";
import { useErrorNotification } from "@/shared/ui/notifications";
import { PaperButton, PaperDrawer } from "@/shared/ui/paper";
import styles from "./AppShell.module.css";

export function AppShell({
  appearance,
  onToggleAppearance,
}: {
  appearance: Appearance;
  onToggleAppearance: () => void;
}) {
  const { staff } = useStaffSession();
  const training = useEmployeeTraining();
  if (!staff) return null;
  return (
    <ReleaseNotesProvider
      key={staff.staffId}
      staffId={staff.staffId}
      canViewHistory={staff.role === "superuser" || staff.role === "administrator"}
      enabled={training.ready && !training.active}
    >
      <ShellContent appearance={appearance} onToggleAppearance={onToggleAppearance} />
    </ReleaseNotesProvider>
  );
}

function ShellContent({
  appearance,
  onToggleAppearance,
}: {
  appearance: Appearance;
  onToggleAppearance: () => void;
}) {
  const { staff, signOut, error } = useStaffSession();
  const [signingOut, setSigningOut] = useState(false);
  const [logoutFailed, setLogoutFailed] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false);
  const navigation = (collapsed: boolean) => (
    <>
      <NavLink
        to="/workspace"
        title={collapsed ? "Главная" : undefined}
        aria-label="Главная"
        onClick={() => setMenuOpen(false)}
      >
        <Icon name="home" /> <span className={styles.navigationLabel}>Главная</span>
      </NavLink>
      {staff?.permissions.some((grant) => grant.permission === "clientsRead" && grant.allowed) && (
        <NavLink
          to="/clients"
          title={collapsed ? "Клиенты" : undefined}
          aria-label="Клиенты"
          onClick={() => setMenuOpen(false)}
        >
          <Icon name="users" /> <span className={styles.navigationLabel}>Клиенты</span>
        </NavLink>
      )}
      <NavLink
        to="/learning"
        title={collapsed ? "Обучение" : undefined}
        aria-label="Обучение"
        onClick={() => setMenuOpen(false)}
      >
        <Icon name="book" /> <span className={styles.navigationLabel}>Обучение</span>
      </NavLink>
    </>
  );
  const sessionError = error
    ? `${error.message}${error.traceId ? ` · Код обращения: ${error.traceId}` : ""}`
    : logoutFailed
      ? "Не удалось завершить сессию."
      : null;
  useErrorNotification(sessionError, {
    title: "Ошибка сессии",
  });

  async function leave() {
    setSigningOut(true);
    setLogoutFailed(false);
    try {
      await signOut();
    } catch {
      setLogoutFailed(true);
    } finally {
      setSigningOut(false);
    }
  }

  return (
    <div
      className={`${styles.workspaceLayout} ${sidebarCollapsed ? styles.workspaceLayoutCollapsed : ""}`}
    >
      <aside className={styles.workspaceSidebar}>
        <NavLink to="/workspace" className={styles.brandLink ?? ""}>
          <Brand compact={sidebarCollapsed} />
        </NavLink>
        <nav
          id="desktop-app-navigation"
          className={styles.workspaceNavigation}
          aria-label="Разделы приложения"
        >
          {navigation(sidebarCollapsed)}
        </nav>
        <div className={styles.workspaceVersionFooter}>
          <ReleaseVersion compact={sidebarCollapsed} />
        </div>
      </aside>

      <div className={styles.workspaceMain}>
        <header className={styles.workspaceHeader}>
          <PaperButton
            className={styles.desktopMenuToggle}
            title={sidebarCollapsed ? "Развернуть меню" : undefined}
            aria-label={sidebarCollapsed ? "Развернуть меню" : "Свернуть меню"}
            aria-expanded={!sidebarCollapsed}
            aria-controls="desktop-app-navigation"
            icon={<Icon name={sidebarCollapsed ? "next" : "previous"} />}
            onClick={() => setSidebarCollapsed((collapsed) => !collapsed)}
          />
          <div className={styles.mobileBrand}>
            <PaperButton
              className={styles.mobileMenuTrigger}
              aria-label="Открыть меню"
              aria-expanded={menuOpen}
              aria-controls="mobile-app-navigation"
              icon={<Icon name="menu" />}
              onClick={() => setMenuOpen(true)}
            />
            <Brand />
          </div>
          <div className={styles.headerActions}>
            <Menu.Root>
              <Menu.Trigger
                type="button"
                disabled={signingOut}
                aria-label={`Меню пользователя ${staff?.login ?? ""}`}
                className={styles.accountMenuTrigger}
              >
                <span className={styles.accountGreeting}>Привет, {staff?.login}</span>
                {signingOut ? <span className={styles.signOutLoading} aria-hidden="true" /> : null}
                <Icon name="chevronDown" size={16} />
              </Menu.Trigger>
              <Menu.Portal>
                <Menu.Positioner
                  className={styles.menuPositioner}
                  side="bottom"
                  align="end"
                  sideOffset={6}
                >
                  <Menu.Popup className={styles.accountMenu} data-popup-scroll-boundary>
                    <Menu.Item
                      className={styles.menuItem}
                      disabled={signingOut}
                      onClick={onToggleAppearance}
                    >
                      <Icon name={appearance === "light" ? "moon" : "sun"} />
                      {appearance === "light" ? "Тёмная тема" : "Светлая тема"}
                    </Menu.Item>
                    <Menu.Separator className={styles.menuSeparator} />
                    <Menu.Item
                      className={`${styles.menuItem} ${styles.dangerItem}`}
                      disabled={signingOut}
                      onClick={() => void leave()}
                    >
                      <Icon name="logout" />
                      Выйти
                    </Menu.Item>
                  </Menu.Popup>
                </Menu.Positioner>
              </Menu.Portal>
            </Menu.Root>
          </div>
        </header>

        <PaperDrawer
          title="Меню"
          open={menuOpen}
          onClose={() => setMenuOpen(false)}
          className={styles.mobileMenu}
          footer={<ReleaseVersion onOpen={() => setMenuOpen(false)} />}
        >
          <nav
            id="mobile-app-navigation"
            className={`${styles.workspaceNavigation} ${styles.mobileNavigation}`}
            aria-label="Разделы приложения на телефоне"
          >
            {navigation(false)}
          </nav>
        </PaperDrawer>

        <main id="main-content" className={styles.workspaceContent}>
          <Outlet />
        </main>
      </div>
    </div>
  );
}
