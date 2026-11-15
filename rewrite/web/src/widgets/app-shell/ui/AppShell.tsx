import { Alert, Button, Dropdown } from "antd";
import { useState } from "react";
import { NavLink, Outlet } from "react-router";
import { useStaffSession } from "@/entities/session";
import { useEmployeeTraining } from "@/features/employee-training";
import { ReleaseNotesProvider, ReleaseVersion } from "@/features/view-release-notes";
import type { Appearance } from "@/shared/lib/appearance";
import { Brand } from "@/shared/ui/Brand";
import { Icon } from "@/shared/ui/Icon";
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
  const { staff, signOut, error, clearError } = useStaffSession();
  const [signingOut, setSigningOut] = useState(false);
  const [logoutFailed, setLogoutFailed] = useState(false);

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
    <div className="workspace-layout">
      <aside className="workspace-sidebar">
        <NavLink to="/workspace" className="brand-link">
          <Brand />
        </NavLink>
        <nav className="workspace-navigation" aria-label="Разделы приложения">
          <NavLink to="/workspace">
            <Icon name="home" /> Главная
          </NavLink>
          {staff?.permissions.some(
            (grant) => grant.permission === "clientsRead" && grant.allowed,
          ) && (
            <NavLink to="/clients">
              <Icon name="users" /> Клиенты
            </NavLink>
          )}
          <NavLink to="/learning">
            <Icon name="book" /> Обучение
          </NavLink>
        </nav>
        <div className="workspace-version-footer">
          <ReleaseVersion />
        </div>
      </aside>

      <div className="workspace-main">
        <header className="workspace-header">
          <div className="mobile-brand">
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
                <span aria-hidden="true">▾</span>
              </Button>
            </Dropdown>
          </div>
        </header>

        <nav className="mobile-navigation" aria-label="Разделы приложения на телефоне">
          <NavLink to="/workspace">
            <Icon name="home" /> Главная
          </NavLink>
          {staff?.permissions.some(
            (grant) => grant.permission === "clientsRead" && grant.allowed,
          ) && (
            <NavLink to="/clients">
              <Icon name="users" /> Клиенты
            </NavLink>
          )}
          <NavLink to="/learning">
            <Icon name="book" /> Обучение
          </NavLink>
        </nav>

        <main id="main-content" className="workspace-content">
          {(error || logoutFailed) && (
            <Alert
              type="error"
              showIcon
              title={error?.message ?? "Не удалось завершить сессию"}
              description={error?.traceId ? `Код обращения: ${error.traceId}` : undefined}
              closable
              onClose={() => {
                clearError();
                setLogoutFailed(false);
              }}
            />
          )}
          <Outlet />
        </main>
        <div className="mobile-release-footer">
          <ReleaseVersion />
        </div>
      </div>
    </div>
  );
}
