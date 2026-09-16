import { NavLink, useNavigate } from "react-router";

import { PortalNotificationBell } from "@/entities/notification";
import { useAuth } from "@/entities/session";
import { PushNotificationControl } from "@/features/manage-push-notifications";
import { LogoutOutlined } from "@/shared/ui/icons";
import { RpgButton, type RpgIconName, RpgNavigation, RpgNavItem } from "@/shared/ui/rpg";
import { ClientPortalShell } from "@/widgets/client-portal-shell";
import { PortalHeader } from "@/widgets/portal-header";

const navigation: { path: string; label: string; icon: RpgIconName }[] = [
  { path: "/portal", label: "Главная", icon: "pick" },
  { path: "/portal/courses", label: "Курсы", icon: "scroll" },
  { path: "/portal/homework", label: "Домашние задания", icon: "quill" },
  { path: "/portal/trainers", label: "Тренажёры", icon: "guitar" },
  { path: "/portal/leaderboards", label: "Рейтинги", icon: "laurel" },
  { path: "/portal/schedule", label: "Расписание", icon: "scroll" },
  { path: "/portal/vacations", label: "Отпуска", icon: "flower" },
];

export function PortalLayout() {
  const auth = useAuth();
  const navigate = useNavigate();

  const logout = () => {
    void navigate("/portal/signed-out", { replace: true });
    void auth.logout();
  };

  return (
    <ClientPortalShell
      header={
        <>
          <PortalHeader
            alias={auth.user?.portalAlias ?? ""}
            actions={
              <>
                <PortalNotificationBell footer={<PushNotificationControl appearance="portal" />} />
                <RpgButton aria-label="Выйти" title="Выйти" onClick={logout}>
                  <LogoutOutlined />
                </RpgButton>
              </>
            }
            navigation={
              <RpgNavigation>
                {navigation.map(({ path, label, icon }) => (
                  <NavLink key={path} to={path} end={path === "/portal"}>
                    <RpgNavItem icon={icon}>{label}</RpgNavItem>
                  </NavLink>
                ))}
              </RpgNavigation>
            }
          />
        </>
      }
    />
  );
}
