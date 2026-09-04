import type { ItemType } from "antd/es/menu/interface";
import type { ReactNode } from "react";

import { FileSearchOutlined, LogoutOutlined, MoonOutlined, SettingOutlined, SunOutlined } from "@/shared/ui/icons";
import { Shortcut } from "@/shared/ui/Shortcut";

import { type AppNavItem, navGroupIcons, navGroupLabels } from "./navigation";

export type ShellActionKey = "profile" | "audit" | "theme" | "logout";

type ShellActionItemOptions = {
  canViewAudit?: boolean;
  isDarkMode: boolean;
};

type BuildNavMenuItemsOptions = {
  showShortcuts?: boolean;
  groupedPopupLabels?: boolean;
  submenuPopupClassName?: string;
};

export function buildNavMenuItems(items: AppNavItem[], options: BuildNavMenuItemsOptions = {}): ItemType[] {
  const { showShortcuts = true, groupedPopupLabels = false, submenuPopupClassName } = options;
  const ungroupedItems = items.filter((item) => !item.group).map((item) => buildNavItem(item, showShortcuts));
  const statsItems = items.filter((item) => item.group === "stats").map((item) => buildNavItem(item, showShortcuts));
  const referenceBookItems = items.filter((item) => item.group === "reference-books").map((item) => buildNavItem(item, showShortcuts));

  return [
    ...ungroupedItems,
    ...(statsItems.length > 0
      ? [
          {
            key: "group:stats",
            icon: navGroupIcons.stats,
            label: groupedPopupLabels ? navGroupLabels.stats : navGroupLabels.stats,
            popupClassName: submenuPopupClassName,
            children: statsItems,
          },
        ]
      : []),
    ...(referenceBookItems.length > 0
      ? [
          {
            key: "group:reference-books",
            icon: navGroupIcons["reference-books"],
            label: groupedPopupLabels ? navGroupLabels["reference-books"] : navGroupLabels["reference-books"],
            popupClassName: submenuPopupClassName,
            children: referenceBookItems,
          },
        ]
      : []),
  ];
}

function buildNavItem(item: AppNavItem, showShortcuts: boolean): ItemType {
  return {
    key: item.key,
    icon: item.icon,
    label: (
      <span className="app-nav-label" data-nav-route={item.key}>
        <span>{item.label}</span>
        {showShortcuts ? <Shortcut keyb={item.shortcut} /> : null}
      </span>
    ),
  };
}

export function buildShellActionItems({ canViewAudit = false, isDarkMode }: ShellActionItemOptions): ItemType[] {
  return [
    {
      key: "profile",
      icon: <SettingOutlined />,
      label: (
        <span className="app-nav-label">
          <span>Профиль</span>
          <Shortcut keyb={"U"} />
        </span>
      ),
    },
    ...(canViewAudit
      ? [
          {
            key: "audit",
            icon: <FileSearchOutlined />,
            label: (
              <span className="app-nav-label">
                <span>Аудит</span>
                <Shortcut keyb={"I"} />
              </span>
            ),
          } satisfies ItemType,
        ]
      : []),
    {
      key: "theme",
      icon: isDarkMode ? <SunOutlined /> : <MoonOutlined />,
      label: (
        <span className="app-nav-label">
          <span>{isDarkMode ? "Светлая тема" : "Темная тема"}</span>
          <Shortcut keyb={"T"} />
        </span>
      ),
    },
    {
      key: "logout",
      icon: <LogoutOutlined />,
      label: (
        <span className="app-nav-label">
          <span>Выйти</span>
        </span>
      ),
      danger: true,
    },
  ];
}

export function getSelectedNavKey(items: Pick<AppNavItem, "key">[], pathname: string) {
  return items.find((item) => item.key !== "/" && pathname.startsWith(item.key))?.key ?? "/";
}

export function renderUserName(firstName?: string | null, lastName?: string | null): ReactNode {
  return (
    <>
      {firstName} {lastName}
    </>
  );
}
