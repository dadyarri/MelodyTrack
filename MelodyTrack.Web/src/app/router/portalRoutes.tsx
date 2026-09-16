import type { RouteObject } from "react-router";

import { ClientPortalThemeProvider } from "@/shared/config";
import { recoverableImport } from "@/shared/lib";

import { ClientPortalAliasRoute, ClientPortalRoute } from "./guards/ClientPortalRoute";
import { RouteErrorBoundary } from "./RouteErrorBoundary";

export const portalRoutes: RouteObject[] = [
  {
    path: "/portal/alias",
    errorElement: <RouteErrorBoundary />,
    lazy: async () => {
      const { ClientPortalAliasPage } = await recoverableImport(() => import("@/pages/client-portal-alias"));
      return {
        Component: () => (
          <ClientPortalThemeProvider>
            <ClientPortalAliasRoute>
              <ClientPortalAliasPage />
            </ClientPortalAliasRoute>
          </ClientPortalThemeProvider>
        ),
      };
    },
  },
  {
    path: "/portal/error",
    errorElement: <RouteErrorBoundary />,
    lazy: async () => {
      const { PortalAccessPage } = await recoverableImport(() => import("@/pages/portal-access"));
      return {
        Component: () => (
          <ClientPortalThemeProvider>
            <PortalAccessPage />
          </ClientPortalThemeProvider>
        ),
      };
    },
  },
  {
    path: "/portal/signed-out",
    errorElement: <RouteErrorBoundary />,
    lazy: async () => {
      const { PortalAccessPage } = await recoverableImport(() => import("@/pages/portal-access"));
      return {
        Component: () => (
          <ClientPortalThemeProvider>
            <PortalAccessPage signedOut />
          </ClientPortalThemeProvider>
        ),
      };
    },
  },
  {
    path: "/portal",
    errorElement: <RouteErrorBoundary />,
    lazy: async () => {
      const { PortalLayout } = await recoverableImport(() => import("./PortalLayout"));

      return {
        Component: () => (
          <ClientPortalThemeProvider>
            <ClientPortalRoute>
              <PortalLayout />
            </ClientPortalRoute>
          </ClientPortalThemeProvider>
        ),
      };
    },
    children: [
      {
        index: true,
        lazy: async () => {
          const { ClientPortalHomePage } = await recoverableImport(() => import("@/pages/client-portal-home"));
          return { Component: ClientPortalHomePage };
        },
      },
      {
        path: "courses",
        lazy: async () => {
          const { ClientPortalCoursesPage } = await recoverableImport(() => import("@/pages/client-portal-learning"));
          return { Component: ClientPortalCoursesPage };
        },
      },
      {
        path: "homework",
        lazy: async () => {
          const { ClientPortalHomeworkPage } = await recoverableImport(() => import("@/pages/client-portal-learning"));
          return { Component: ClientPortalHomeworkPage };
        },
      },
      {
        path: "trainers",
        lazy: async () => {
          const { ClientPortalTrainersPage } = await recoverableImport(() => import("@/pages/client-portal-learning"));
          return { Component: ClientPortalTrainersPage };
        },
      },
      {
        path: "leaderboards",
        lazy: async () => {
          const { ClientPortalLeaderboardsPage } = await recoverableImport(() => import("@/pages/client-portal-learning"));
          return { Component: ClientPortalLeaderboardsPage };
        },
      },
      {
        path: "schedule",
        lazy: async () => {
          const { ClientPortalSchedulePage } = await recoverableImport(() => import("@/pages/client-portal-schedule"));

          return {
            Component: ClientPortalSchedulePage,
          };
        },
      },
      {
        path: "vacations",
        lazy: async () => {
          const { ClientPortalVacationsPage } = await recoverableImport(() => import("@/pages/client-portal-vacations"));

          return {
            Component: ClientPortalVacationsPage,
          };
        },
      },
    ],
  },
];
