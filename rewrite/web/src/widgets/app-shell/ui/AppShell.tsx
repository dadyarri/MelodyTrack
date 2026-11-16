import { Button, Drawer, Dropdown } from "antd";
import { useState } from "react";
import { NavLink, Outlet } from "react-router";
import { useStaffSession } from "@/entities/session";
import { useEmployeeTraining } from "@/features/employee-training";
import { OfflineStorageDialog } from "@/features/manage-offline-storage";
import { ReleaseNotesProvider, ReleaseVersion } from "@/features/view-release-notes";
import type { Appearance } from "@/shared/lib/appearance";
import { Brand } from "@/shared/ui/Brand";
import { Icon } from "@/shared/ui/Icon";
import { useErrorNotification } from "@/shared/ui/notifications";
import "./app-shell.css";

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
  const [offlineOpen, setOfflineOpen] = useState(false);
  const navigation = (collapsed: boolean) => (
    <>
      <NavLink
        to="/workspace"
        title={collapsed ? "Главная" : undefined}
        aria-label="Главная"
        onClick={() => setMenuOpen(false)}
      >
        <Icon name="home" /> <span className="navigation-label">Главная</span>
      </NavLink>
      {staff?.permissions.some((grant) => grant.permission === "clientsRead" && grant.allowed) && (
        <NavLink
          to="/clients"
          title={collapsed ? "Клиенты" : undefined}
          aria-label="Клиенты"
          onClick={() => setMenuOpen(false)}
        >
          <Icon name="users" /> <span className="navigation-label">Клиенты</span>
        </NavLink>
      )}
      <NavLink
        to="/learning"
        title={collapsed ? "Обучение" : undefined}
        aria-label="Обучение"
        onClick={() => setMenuOpen(false)}
      >
        <Icon name="book" /> <span className="navigation-label">Обучение</span>
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
    <div className={`workspace-layout${sidebarCollapsed ? " workspace-layout-collapsed" : ""}`}>
      <aside className="workspace-sidebar">
        <NavLink to="/workspace" className="brand-link">
          <Brand compact={sidebarCollapsed} />
        </NavLink>
        <nav
          id="desktop-app-navigation"
          className="workspace-navigation"
          aria-label="Разделы приложения"
        >
          {navigation(sidebarCollapsed)}
        </nav>
        <div className="workspace-version-footer">
          <ReleaseVersion compact={sidebarCollapsed} />
        </div>
      </aside>

      <div className="workspace-main">
        <header className="workspace-header">
          <Button
            type="text"
            className="desktop-menu-toggle"
            title={sidebarCollapsed ? "Развернуть меню" : undefined}
            aria-label={sidebarCollapsed ? "Развернуть меню" : "Свернуть меню"}
            aria-expanded={!sidebarCollapsed}
            aria-controls="desktop-app-navigation"
            icon={<Icon name={sidebarCollapsed ? "next" : "previous"} />}
            onClick={() => setSidebarCollapsed((collapsed) => !collapsed)}
          />
          <div className="mobile-brand">
            <Button
              type="text"
              aria-label="Открыть меню"
              aria-expanded={menuOpen}
              aria-controls="mobile-app-navigation"
              icon={<Icon name="menu" />}
              onClick={() => setMenuOpen(true)}
            />
            <Brand />
          </div>
          <div className="header-actions">
            <Dropdown
              trigger={["click"]}
              placement="bottomRight"
              disabled={signingOut}
              menu={{
                className: "account-menu",
                selectable: false,
                items: [
                  {
                    key: "offline",
                    label: "Локальные данные",
                  },
                  {
                    key: "theme",
                    label: appearance === "light" ? "Тёмная тема" : "Светлая тема",
                    icon: <Icon name={appearance === "light" ? "moon" : "sun"} />,
                    disabled: signingOut,
                  },
                  { type: "divider" },
                  {
                    key: "logout",
                    label: "Выйти",
                    icon: <Icon name="logout" />,
                    danger: true,
                    disabled: signingOut,
                  },
                ],
                onClick: ({ key }) => {
                  if (key === "theme") onToggleAppearance();
                  if (key === "offline") setOfflineOpen(true);
                  if (key === "logout") void leave();
                },
              }}
            >
              <Button
                type="text"
                className="account-menu-trigger"
                loading={signingOut}
                aria-label={`Меню пользователя ${staff?.login ?? ""}`}
              >
                <span className="account-greeting">Привет, {staff?.login}</span>
                <Icon name="chevronDown" size={16} />
              </Button>
            </Dropdown>
          </div>
        </header>

        <Drawer
          title="Меню"
          placement="left"
          open={menuOpen}
          onClose={() => setMenuOpen(false)}
          className="workspace-mobile-menu"
          size="min(360px, 100vw)"
          closeIcon={<Icon name="close" />}
          footer={<ReleaseVersion onOpen={() => setMenuOpen(false)} />}
        >
          <nav
            id="mobile-app-navigation"
            className="workspace-navigation"
            aria-label="Разделы приложения на телефоне"
          >
            {navigation(false)}
          </nav>
        </Drawer>

        <main id="main-content" className="workspace-content">
          <Outlet />
        </main>
        <OfflineStorageDialog open={offlineOpen} onClose={() => setOfflineOpen(false)} />
      </div>
    </div>
  );
}
