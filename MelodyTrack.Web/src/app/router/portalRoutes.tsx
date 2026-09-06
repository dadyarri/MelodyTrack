import { Navigate, type RouteObject } from "react-router";

import { ClientPortalThemeProvider } from "@/shared/config";
import { recoverableImport } from "@/shared/lib";

import { ClientPortalRoute } from "./guards/ClientPortalRoute";
import { RouteErrorBoundary } from "./RouteErrorBoundary";

export const portalRoutes: RouteObject[] = [
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
    path: "/portal",
    errorElement: <RouteErrorBoundary />,
    lazy: async () => {
      const { ClientPortalShell } = await recoverableImport(() => import("@/widgets/client-portal-shell"));

      return {
        Component: () => (
          <ClientPortalThemeProvider>
            <ClientPortalRoute>
              <ClientPortalShell />
            </ClientPortalRoute>
          </ClientPortalThemeProvider>
        ),
      };
    },
    children: [
      {
        index: true,
        element: <Navigate to="/portal/schedule" replace />,
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
