import { Button, Space, Typography } from "antd";
import { NavLink, Outlet } from "react-router";

import { useAuth } from "@/entities/session";
import { CalendarOutlined, LogoutOutlined } from "@/shared/ui/icons";

import styles from "./ClientPortalShell.module.css";

export function ClientPortalShell() {
  const auth = useAuth();

  return (
    <div className={styles.shell}>
      <div className={styles.inner}>
        <header className={styles.header}>
          <div>
            <Typography.Text className={styles.eyebrow}>MelodyTrack Portal</Typography.Text>
            <Typography.Title level={2} className={styles.title}>
              {auth.user?.firstName}, добро пожаловать!
            </Typography.Title>
          </div>
          <Space className={styles.actions}>
            <Typography.Text type="secondary">
              {auth.user?.firstName} {auth.user?.lastName}
            </Typography.Text>
            <Button icon={<LogoutOutlined />} onClick={() => void auth.logout()}>
              Выйти
            </Button>
          </Space>
        </header>

        <nav className={styles.nav}>
          <NavItem to="/portal/schedule" icon={<CalendarOutlined />} label="Расписание" />
        </nav>

        <main className={styles.content}>
          <Outlet />
        </main>
      </div>
    </div>
  );
}

function NavItem({ to, icon, label }: { to: string; icon: React.ReactNode; label: string }) {
  return (
    <NavLink to={to} className={({ isActive }) => `${styles.navLink}${isActive ? ` ${styles.navLinkActive}` : ""}`}>
      {icon}
      <span>{label}</span>
    </NavLink>
  );
}
