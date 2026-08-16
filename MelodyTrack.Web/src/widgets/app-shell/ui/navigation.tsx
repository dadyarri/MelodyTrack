import type { ReactNode } from "react";

import { type AccessAudience, type AppUser, canAccessAudience } from "@/entities/session";
import {
  BookOutlined,
  CalendarCheckOutlined,
  CalendarOutlined,
  CreditCardOutlined,
  DashboardOutlined,
  FolderOpenOutlined,
  FunnelTargetOutlined,
  ListTodoOutlined,
  PieChartOutlined,
  TagsOutlined,
  TeamOutlined,
  ToolOutlined,
  UserBadgeOutlined,
  WalletOutlined,
} from "@/shared/ui/icons";

type NavGroup = "stats" | "reference-books";

export type AppNavItem = {
  key: string;
  icon: ReactNode;
  label: string;
  shortcut: string;
  visibility: AccessAudience;
  group?: NavGroup;
};

export const navGroupLabels = {
  stats: "Статистика",
  "reference-books": "Справочники",
} as const;

export const navGroupIcons: Record<NavGroup, ReactNode> = {
  stats: <PieChartOutlined />,
  "reference-books": <FolderOpenOutlined />,
};

export const appNavItems: AppNavItem[] = [
  {
    key: "/",
    icon: <DashboardOutlined />,
    label: "Обзор",
    shortcut: "1",
    visibility: "all",
  },
  {
    key: "/tasks",
    icon: <ListTodoOutlined />,
    label: "Задачи",
    shortcut: "2",
    visibility: "admin",
  },
  {
    key: "/statistics/work",
    icon: <CalendarCheckOutlined />,
    label: "Работа",
    shortcut: "W",
    visibility: "stats",
    group: "stats",
  },
  {
    key: "/statistics/finance",
    icon: <PieChartOutlined />,
    label: "Финансы",
    shortcut: "F",
    visibility: "stats",
    group: "stats",
  },
  {
    key: "/statistics/clients",
    icon: <TeamOutlined />,
    label: "Клиенты",
    shortcut: "C",
    visibility: "stats",
    group: "stats",
  },
  {
    key: "/schedule",
    icon: <CalendarOutlined />,
    label: "Расписание",
    shortcut: "3",
    visibility: "all",
  },
  {
    key: "/clients",
    icon: <TeamOutlined />,
    label: "Клиенты",
    shortcut: "4",
    visibility: "admin",
  },
  {
    key: "/courses",
    icon: <BookOutlined />,
    label: "Курсы",
    shortcut: "K",
    visibility: "admin",
    group: "reference-books",
  },
  {
    key: "/services",
    icon: <ToolOutlined />,
    label: "Услуги",
    shortcut: "7",
    visibility: "admin",
    group: "reference-books",
  },
  {
    key: "/payments",
    icon: <CreditCardOutlined />,
    label: "Платежи",
    shortcut: "5",
    visibility: "admin",
  },
  {
    key: "/expenses",
    icon: <WalletOutlined />,
    label: "Расходы",
    shortcut: "6",
    visibility: "admin",
  },
  {
    key: "/users",
    icon: <UserBadgeOutlined />,
    label: "Пользователи",
    shortcut: "8",
    visibility: "admin",
    group: "reference-books",
  },
  {
    key: "/expense-categories",
    icon: <TagsOutlined />,
    label: "Статьи расходов",
    shortcut: "9",
    visibility: "admin",
    group: "reference-books",
  },
  {
    key: "/client-sources",
    icon: <FunnelTargetOutlined />,
    label: "Источники клиентов",
    shortcut: "0",
    visibility: "admin",
    group: "reference-books",
  },
];

export function getAvailableNavItems(user: AppUser) {
  return appNavItems.filter((item) => canAccessAudience(user, item.visibility));
}

export function buildNavigationTarget(targetPath: string, currentPath: string, currentSearch: string) {
  const movesBetweenStatisticsAreas = targetPath.startsWith("/statistics/") && currentPath.startsWith("/statistics/");
  return movesBetweenStatisticsAreas && currentSearch ? `${targetPath}${currentSearch}` : targetPath;
}
