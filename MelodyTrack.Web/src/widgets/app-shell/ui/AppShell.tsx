import { useQueryClient } from "@tanstack/react-query";
import { Button, Divider, Drawer, Layout, Menu, Popover, Space, Typography } from "antd";
import { lazy, type ReactNode, Suspense, type SyntheticEvent, useCallback, useEffect, useState } from "react";
import { useLocation, useNavigate } from "react-router";

import { hasSuperuserAccess, useAuth } from "@/entities/session";
import type { OnboardingDisplayStatus } from "@/features/onboarding";
import {
  canViewReleaseNotes,
  isReleaseNotesEligiblePath,
  ReleaseNotesModal,
  ReleaseVersion,
  useReleaseNotesController,
} from "@/features/view-release-notes";
import { useTheme } from "@/shared/config";
import { clearNavigationIntent, isShortcutTarget, matchesPlainKey, recoverableImport, rememberNavigationIntent } from "@/shared/lib";
import {
  FileSearchOutlined,
  InfoCircleOutlined,
  LeftOutlined,
  LogoutOutlined,
  MenuOutlined,
  MoonOutlined,
  RightOutlined,
  SettingOutlined,
  SunOutlined,
  UserOutlined,
} from "@/shared/ui/icons";

import styles from "./AppShell.module.css";
import { buildNavigationTarget, getAvailableNavItems } from "./navigation";
import { buildNavMenuItems, buildShellActionItems, getSelectedNavKey, renderUserName, type ShellActionKey } from "./shellMenus";

const AppOnboarding = lazy(async () => {
  const module = await recoverableImport(() => import("@/features/onboarding"));

  return { default: module.AppOnboarding };
});

export function AppShell({
  children,
  onPrefetchRoute,
}: {
  children?: ReactNode;
  onPrefetchRoute?: (path: string, queryClient: ReturnType<typeof useQueryClient>) => Promise<void> | void;
}) {
  const auth = useAuth();
  const queryClient = useQueryClient();
  const { mode, toggleMode } = useTheme();
  const navigate = useNavigate();
  const location = useLocation();
  const [mobileNavOpen, setMobileNavOpen] = useState(false);
  const [desktopNavOpen, setDesktopNavOpen] = useState(true);
  const [onboardingStatus, setOnboardingStatus] = useState<OnboardingDisplayStatus>("loading");
  const releaseNotesVisible = canViewReleaseNotes(auth.user);
  const releaseNotes = useReleaseNotesController({
    userId: auth.user?.id ?? null,
    automaticEnabled: releaseNotesVisible && onboardingStatus === "idle" && isReleaseNotesEligiblePath(location.pathname),
  });
  const availableNavItems = getAvailableNavItems(auth.user);
  const canViewAudit = hasSuperuserAccess(auth.user);
  const menuItems = buildNavMenuItems(availableNavItems, {
    showShortcuts: true,
    groupedPopupLabels: !desktopNavOpen,
    submenuPopupClassName: !desktopNavOpen ? styles.desktopNavSubmenuPopup : undefined,
  });
  const mobileMenuItems = buildNavMenuItems(availableNavItems, {
    showShortcuts: false,
  });
  const mobileActionItems = buildShellActionItems({
    canViewAudit,
    isDarkMode: mode === "dark",
  });
  const desktopUserActionItems = [
    ...mobileActionItems,
    ...(releaseNotesVisible
      ? [{ type: "divider" as const }, { key: "releaseNotes", icon: <InfoCircleOutlined />, label: "Что нового" }]
      : []),
  ];
  const mobileDrawerActionItems = [
    { key: "profile", icon: <SettingOutlined />, label: "Профиль" },
    ...(canViewAudit ? [{ key: "audit", icon: <FileSearchOutlined />, label: "Аудит" }] : []),
    {
      key: "theme",
      icon: mode === "dark" ? <SunOutlined /> : <MoonOutlined />,
      label: mode === "dark" ? "Светлая тема" : "Темная тема",
    },
    { key: "logout", icon: <LogoutOutlined />, label: "Выйти", danger: true },
    ...(releaseNotesVisible
      ? [{ type: "divider" as const }, { key: "releaseNotes", icon: <InfoCircleOutlined />, label: "Что нового" }]
      : []),
  ];
  const navigateTo = useCallback(
    (path: string) => {
      const target = buildNavigationTarget(path, location.pathname, location.search);
      rememberNavigationIntent(target);
      void navigate(target);
    },
    [location.pathname, location.search, navigate],
  );
  const handleUserAction = (key: ShellActionKey | "releaseNotes") => {
    if (key === "releaseNotes") {
      releaseNotes.openManual();
      return;
    }
    if (key === "profile") {
      navigateTo("/profile");
      return;
    }

    if (key === "audit") {
      navigateTo("/audit");
      return;
    }

    if (key === "theme") {
      toggleMode();
      return;
    }

    void auth.logout();
  };
  const selectedKey = getSelectedNavKey(availableNavItems, location.pathname);
  const prefetchNavigationTarget = useCallback(
    (event: SyntheticEvent<HTMLElement>) => {
      if (!(event.target instanceof Element)) {
        return;
      }

      const path = event.target.closest<HTMLElement>("[data-nav-route]")?.dataset.navRoute;
      if (path) {
        void onPrefetchRoute?.(path, queryClient);
      }
    },
    [onPrefetchRoute, queryClient],
  );

  useEffect(() => {
    clearNavigationIntent(location.pathname + location.search);
  }, [location.pathname, location.search]);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.defaultPrevented || event.repeat || isShortcutTarget(event.target)) {
        return;
      }

      if (isShellShortcutReserved(location.pathname, event.key)) {
        return;
      }

      const navItem = availableNavItems.find((item) => matchesPlainKey(event, item.shortcut));
      if (navItem) {
        event.preventDefault();
        navigateTo(navItem.key);
        return;
      }

      if (matchesPlainKey(event, "u")) {
        event.preventDefault();
        navigateTo("/profile");
        return;
      }

      if (canViewAudit && matchesPlainKey(event, "i")) {
        event.preventDefault();
        navigateTo("/audit");
        return;
      }

      if (matchesPlainKey(event, "t")) {
        event.preventDefault();
        toggleMode();
        return;
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => {
      window.removeEventListener("keydown", handleKeyDown);
    };
  }, [availableNavItems, canViewAudit, location.pathname, navigateTo, toggleMode]);

  return (
    <Layout className={styles.shell}>
      <Layout.Sider
        width={296}
        collapsedWidth={88}
        className={styles.sider}
        breakpoint="lg"
        collapsible
        trigger={null}
        collapsed={!desktopNavOpen}
        onCollapse={(collapsed) => {
          setDesktopNavOpen(!collapsed);
        }}
      >
        {/* <div className={styles.brand} data-onboarding-id="shell-brand">
          MelodyTrack
        </div> */}
        <div
          data-onboarding-id="shell-navigation"
          onFocusCapture={prefetchNavigationTarget}
          onPointerOver={prefetchNavigationTarget}
          onTouchStart={prefetchNavigationTarget}
        >
          <Menu
            mode="inline"
            inlineCollapsed={!desktopNavOpen}
            selectedKeys={[selectedKey]}
            items={menuItems}
            onClick={({ key }) => {
              navigateTo(key);
            }}
          />
        </div>
        <div className={styles.releaseVersion}>
          <ReleaseVersion compact={!desktopNavOpen} />
        </div>
      </Layout.Sider>
      <Layout>
        <Layout.Header className={styles.header}>
          <Button
            type="text"
            className={styles.desktopCollapseButton}
            icon={desktopNavOpen ? <LeftOutlined /> : <RightOutlined />}
            aria-label={desktopNavOpen ? "Свернуть навигацию" : "Развернуть навигацию"}
            onClick={() => {
              setDesktopNavOpen((current) => !current);
            }}
          />
          <Typography.Text className={styles.headerBrand} data-onboarding-id="shell-brand">
            MelodyTrack
          </Typography.Text>
          <Button
            type="text"
            className={styles.mobileMenuButton}
            icon={<MenuOutlined />}
            aria-label="Открыть навигацию"
            onClick={() => {
              setMobileNavOpen(true);
            }}
          />
          <div className={styles.headerSpacer} />
          <Popover
            trigger={["hover", "click"]}
            placement="bottomRight"
            classNames={{ root: styles.headerUserPopover }}
            content={
              <Menu
                mode="inline"
                inlineIndent={10}
                className={styles.headerUserMenu}
                selectable={false}
                items={desktopUserActionItems}
                onClick={({ key }) => {
                  handleUserAction(key as ShellActionKey | "releaseNotes");
                }}
              />
            }
          >
            <button
              type="button"
              className={styles.headerUserTrigger}
              aria-label="Открыть меню пользователя"
              data-onboarding-id="shell-profile-menu"
            >
              <Space size={8}>
                <UserOutlined />
                <Typography.Text>{renderUserName(auth.user?.firstName, auth.user?.lastName)}</Typography.Text>
              </Space>
            </button>
          </Popover>
          <Typography.Text className={styles.headerUserMobile}>
            <Space size={6}>
              <UserOutlined />
              <span>{renderUserName(auth.user?.firstName, auth.user?.lastName)}</span>
            </Space>
          </Typography.Text>
        </Layout.Header>
        <Layout.Content className={styles.content}>
          <Space orientation="vertical" className={styles.contentStack}>
            {children}
          </Space>
        </Layout.Content>
      </Layout>
      <Drawer
        open={mobileNavOpen}
        placement="left"
        size={296}
        onClose={() => {
          setMobileNavOpen(false);
        }}
        className={styles.mobileNavDrawer}
      >
        <Space orientation="vertical" size={16} className="wide">
          <div onFocusCapture={prefetchNavigationTarget} onPointerOver={prefetchNavigationTarget} onTouchStart={prefetchNavigationTarget}>
            <Menu
              mode="inline"
              className={styles.mobileNavMenu}
              selectedKeys={[selectedKey]}
              items={mobileMenuItems}
              onClick={({ key }) => {
                navigateTo(key);
                setMobileNavOpen(false);
              }}
            />
          </div>
          <Divider className="mobile-nav-divider" />
          <Menu
            mode="inline"
            className={styles.mobileNavActionsMenu}
            selectable={false}
            items={mobileDrawerActionItems}
            onClick={({ key }) => {
              setMobileNavOpen(false);
              handleUserAction(key as ShellActionKey | "releaseNotes");
            }}
          />
          <div className={styles.mobileReleaseVersion}>
            <ReleaseVersion />
          </div>
        </Space>
      </Drawer>
      <Suspense fallback={null}>
        <AppOnboarding onStatusChange={setOnboardingStatus} />
      </Suspense>
      {releaseNotesVisible ? (
        <ReleaseNotesModal open={releaseNotes.open} automaticReleases={releaseNotes.automaticReleases} onClose={releaseNotes.close} />
      ) : null}
    </Layout>
  );
}

function isShellShortcutReserved(pathname: string, key: string) {
  const normalizedKey = key.toLowerCase();

  if (pathname.startsWith("/schedule")) {
    return ["a", "m", "arrowleft", "arrowright", "home"].includes(normalizedKey);
  }

  if (pathname.startsWith("/profile")) {
    return ["r", "g", "o", "f", "w"].includes(normalizedKey);
  }

  if (pathname.startsWith("/payments")) {
    return ["a", "x"].includes(normalizedKey);
  }

  if (pathname.startsWith("/expenses")) {
    return ["a", "x"].includes(normalizedKey);
  }

  if (pathname.startsWith("/services")) {
    return normalizedKey === "a";
  }

  if (pathname.startsWith("/clients")) {
    return normalizedKey === "a";
  }

  if (pathname.startsWith("/users")) {
    return normalizedKey === "a";
  }

  if (pathname.startsWith("/expense-categories")) {
    return normalizedKey === "a";
  }

  if (pathname.startsWith("/client-sources")) {
    return normalizedKey === "a";
  }

  if (pathname === "/") {
    return normalizedKey === "x";
  }

  return false;
}
