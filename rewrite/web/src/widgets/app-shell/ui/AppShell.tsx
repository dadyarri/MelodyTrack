import { Alert, Button } from "antd";
import { useState } from "react";
import { NavLink, Outlet, useLocation } from "react-router";
import { useStaffSession } from "@/entities/session";
import { useEmployeeTraining } from "@/features/employee-training";
import type { Appearance } from "@/shared/lib/appearance";
import { Brand } from "@/shared/ui/Brand";
import { Icon } from "@/shared/ui/Icon";

const roleNames: Record<string, string> = {
  superuser: "Суперпользователь",
  administrator: "Администратор",
  teacher: "Преподаватель",
};

export function AppShell({
  appearance,
  onToggleAppearance,
}: {
  appearance: Appearance;
  onToggleAppearance: () => void;
}) {
  const { staff, signOut, error, clearError } = useStaffSession();
  const { openArticle } = useEmployeeTraining();
  const location = useLocation();
  const [signingOut, setSigningOut] = useState(false);
  const [logoutFailed, setLogoutFailed] = useState(false);
  const title = location.pathname === "/learning" ? "Журнал обучения" : "Рабочее пространство";

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
        <div className="sidebar-school">
          <span className="small-dot" /> Музыкальная школа
        </div>
        <nav className="workspace-navigation" aria-label="Разделы приложения">
          <NavLink to="/workspace">
            <Icon name="home" /> Рабочее пространство
          </NavLink>
          <NavLink to="/learning">
            <Icon name="book" /> Журнал обучения
          </NavLink>
        </nav>
        <div className="sidebar-bottom">
          <span className="sidebar-motto">Всё в своём ритме.</span>
        </div>
      </aside>

      <div className="workspace-main">
        <header className="workspace-header">
          <div className="mobile-brand">
            <Brand />
          </div>
          <span className="header-breadcrumb">
            Школа <span>/</span> <strong>{title}</strong>
          </span>
          <div className="header-actions">
            <Button
              type="text"
              icon={<Icon name="book" />}
              onClick={openArticle}
              aria-label="Помощь по входу и сессиям"
            />
            <Button
              type="text"
              icon={<Icon name={appearance === "light" ? "moon" : "sun"} />}
              onClick={onToggleAppearance}
              aria-label={appearance === "light" ? "Включить тёмную тему" : "Включить светлую тему"}
            />
            <div className="staff-chip">
              <span className="staff-avatar">М</span>
              <span>{roleNames[staff?.role ?? ""] ?? "Сотрудник"}</span>
            </div>
            <Button
              type="text"
              icon={<Icon name="logout" />}
              onClick={() => void leave()}
              loading={signingOut}
              aria-label="Выйти из аккаунта"
            />
          </div>
        </header>

        <nav className="mobile-navigation" aria-label="Разделы приложения на телефоне">
          <NavLink to="/workspace">
            <Icon name="home" /> Главная
          </NavLink>
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
              description={
                error?.traceId
                  ? `Код обращения: ${error.traceId}`
                  : "Проверьте соединение и повторите действие."
              }
              closable
              onClose={() => {
                clearError();
                setLogoutFailed(false);
              }}
            />
          )}
          <Outlet />
        </main>
        <footer className="workspace-footer">
          MelodyTrack <span>Рабочее пространство школы</span>
        </footer>
      </div>
    </div>
  );
}
