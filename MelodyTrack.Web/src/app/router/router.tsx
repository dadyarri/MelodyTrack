import type { QueryClient } from "@tanstack/react-query";
import { createBrowserRouter, Navigate, Outlet } from "react-router";

import { recoverableImport } from "@/shared/lib";

import { AdminRoute } from "./guards/AdminRoute";
import { ProtectedRoute } from "./guards/ProtectedRoute";
import { StatsRoute } from "./guards/StatsRoute";
import { SuperuserRoute } from "./guards/SuperuserRoute";
import { portalRoutes } from "./portalRoutes";
import { RouteErrorBoundary } from "./RouteErrorBoundary";
import { createRoutePrefetcher } from "./routePrefetch";

const prefetchStaffRoute = createRoutePrefetcher<QueryClient>(
  {
    "/": () => import("@/pages/dashboard"),
    "/audit": () => import("@/pages/audit"),
    "/alias-dictionaries": () => import("@/pages/alias-dictionaries"),
    "/client-sources": () => import("@/pages/client-sources"),
    "/clients": () => import("@/pages/clients"),
    "/courses": () => import("@/pages/courses"),
    "/expense-categories": () => import("@/pages/expense-categories"),
    "/expenses": () => import("@/pages/expenses"),
    "/payments": () => import("@/pages/payments"),
    "/profile": () => import("@/pages/profile"),
    "/statistics/work": () => import("@/pages/statistics"),
    "/statistics/finance": () => import("@/pages/statistics"),
    "/statistics/clients": () => import("@/pages/statistics"),
    "/schedule": () => import("@/pages/schedule"),
    "/services": () => import("@/pages/services"),
    "/tasks": () => import("@/pages/tasks"),
    "/trainers": () => import("@/pages/trainers"),
    "/users": () => import("@/pages/users"),
    "/vacation-requests": () => import("@/pages/vacation-requests"),
  },
  async (module, queryClient) => {
    if (isPrefetchableRouteModule(module)) {
      await module.prefetchRouteData(queryClient);
    }
  },
);

function isPrefetchableRouteModule(module: unknown): module is { prefetchRouteData: (queryClient: QueryClient) => Promise<unknown> } {
  return typeof module === "object" && module != null && "prefetchRouteData" in module && typeof module.prefetchRouteData === "function";
}

export const router = createBrowserRouter([
  ...(import.meta.env.DEV
    ? [
        {
          path: "/dev/portal-design",
          errorElement: <RouteErrorBoundary />,
          lazy: async () => {
            const { PortalDesignGallery } = await import("../devtools/PortalDesignGallery");
            return { Component: PortalDesignGallery };
          },
        },
      ]
    : []),
  {
    path: "/invite/:inviteCode",
    errorElement: <RouteErrorBoundary />,
    lazy: async () => {
      const { InviteRedirect } = await recoverableImport(() => import("@/pages/invite-redirect"));

      return {
        Component: InviteRedirect,
      };
    },
  },
  {
    path: "/login",
    errorElement: <RouteErrorBoundary />,
    lazy: async () => {
      const { AuthPage } = await recoverableImport(() => import("@/pages/auth"));

      return {
        Component: AuthPage,
      };
    },
  },
  {
    path: "/restore",
    errorElement: <RouteErrorBoundary />,
    lazy: async () => {
      const { RestorePasswordPage } = await recoverableImport(() => import("@/pages/restore-password"));

      return {
        Component: RestorePasswordPage,
      };
    },
  },
  ...portalRoutes,
  {
    path: "/",
    errorElement: <RouteErrorBoundary />,
    lazy: async () => {
      const { AppShell } = await recoverableImport(() => import("@/widgets/app-shell"));

      return {
        Component: () => (
          <ProtectedRoute>
            <AppShell onPrefetchRoute={prefetchStaffRoute}>
              <Outlet />
            </AppShell>
          </ProtectedRoute>
        ),
      };
    },
    children: [
      {
        index: true,
        lazy: async () => {
          const { DashboardPage } = await recoverableImport(() => import("@/pages/dashboard"));

          return {
            Component: DashboardPage,
          };
        },
      },
      {
        path: "statistics/work",
        lazy: async () => {
          const { StatisticsWorkPage } = await recoverableImport(() => import("@/pages/statistics"));
          return {
            Component: () => (
              <StatsRoute>
                <StatisticsWorkPage />
              </StatsRoute>
            ),
          };
        },
      },
      {
        path: "statistics/finance",
        lazy: async () => {
          const { StatisticsFinancePage } = await recoverableImport(() => import("@/pages/statistics"));
          return {
            Component: () => (
              <StatsRoute>
                <StatisticsFinancePage />
              </StatsRoute>
            ),
          };
        },
      },
      {
        path: "statistics/clients",
        lazy: async () => {
          const { StatisticsClientsPage } = await recoverableImport(() => import("@/pages/statistics"));
          return {
            Component: () => (
              <StatsRoute>
                <StatisticsClientsPage />
              </StatsRoute>
            ),
          };
        },
      },
      { path: "revenue", element: <Navigate to="/statistics/finance" replace /> },
      { path: "price-changes", element: <Navigate to="/statistics/finance" replace /> },
      { path: "appointments-stats", element: <Navigate to="/statistics/work" replace /> },
      { path: "clients-stats", element: <Navigate to="/statistics/clients" replace /> },
      { path: "payments-stats", element: <Navigate to="/statistics/finance" replace /> },
      { path: "expenses-dashboard", element: <Navigate to="/statistics/finance" replace /> },
      { path: "expenses-stats", element: <Navigate to="/statistics/finance" replace /> },
      {
        path: "clients",
        lazy: async () => {
          const { ClientsPage } = await recoverableImport(() => import("@/pages/clients"));

          return {
            Component: () => (
              <AdminRoute>
                <ClientsPage />
              </AdminRoute>
            ),
          };
        },
      },
      {
        path: "audit",
        lazy: async () => {
          const { AuditPage } = await recoverableImport(() => import("@/pages/audit"));

          return {
            Component: () => (
              <SuperuserRoute>
                <AuditPage />
              </SuperuserRoute>
            ),
          };
        },
      },
      {
        path: "courses",
        lazy: async () => {
          const { CoursesPage } = await recoverableImport(() => import("@/pages/courses"));

          return {
            Component: () => (
              <AdminRoute>
                <CoursesPage />
              </AdminRoute>
            ),
          };
        },
      },
      {
        path: "services",
        lazy: async () => {
          const { ServicesPage } = await recoverableImport(() => import("@/pages/services"));

          return {
            Component: () => (
              <AdminRoute>
                <ServicesPage />
              </AdminRoute>
            ),
          };
        },
      },
      {
        path: "payments",
        lazy: async () => {
          const { PaymentsPage } = await recoverableImport(() => import("@/pages/payments"));

          return {
            Component: () => (
              <AdminRoute>
                <PaymentsPage />
              </AdminRoute>
            ),
          };
        },
      },
      {
        path: "expenses",
        lazy: async () => {
          const { ExpensesPage } = await recoverableImport(() => import("@/pages/expenses"));

          return {
            Component: () => (
              <AdminRoute>
                <ExpensesPage />
              </AdminRoute>
            ),
          };
        },
      },
      {
        path: "expense-categories",
        lazy: async () => {
          const { ExpenseCategoriesPage } = await recoverableImport(() => import("@/pages/expense-categories"));

          return {
            Component: () => (
              <AdminRoute>
                <ExpenseCategoriesPage />
              </AdminRoute>
            ),
          };
        },
      },
      {
        path: "alias-dictionaries",
        lazy: async () => {
          const { AliasDictionariesPage } = await recoverableImport(() => import("@/pages/alias-dictionaries"));

          return {
            Component: () => (
              <AdminRoute>
                <AliasDictionariesPage />
              </AdminRoute>
            ),
          };
        },
      },
      {
        path: "client-sources",
        lazy: async () => {
          const { ClientSourcesPage } = await recoverableImport(() => import("@/pages/client-sources"));

          return {
            Component: () => (
              <AdminRoute>
                <ClientSourcesPage />
              </AdminRoute>
            ),
          };
        },
      },
      {
        path: "schedule",
        lazy: async () => {
          const { SchedulePage } = await recoverableImport(() => import("@/pages/schedule"));

          return {
            Component: SchedulePage,
          };
        },
      },
      {
        path: "tasks",
        lazy: async () => {
          const { TasksPage } = await recoverableImport(() => import("@/pages/tasks"));

          return {
            Component: () => (
              <AdminRoute>
                <TasksPage />
              </AdminRoute>
            ),
          };
        },
      },
      {
        path: "trainers",
        lazy: async () => {
          const { TrainersPage } = await recoverableImport(() => import("@/pages/trainers"));

          return {
            Component: TrainersPage,
          };
        },
      },
      {
        path: "users",
        lazy: async () => {
          const { UsersPage } = await recoverableImport(() => import("@/pages/users"));

          return {
            Component: () => (
              <AdminRoute>
                <UsersPage />
              </AdminRoute>
            ),
          };
        },
      },
      {
        path: "vacation-requests",
        lazy: async () => {
          const { VacationRequestsPage } = await recoverableImport(() => import("@/pages/vacation-requests"));

          return {
            Component: VacationRequestsPage,
          };
        },
      },
      {
        path: "profile",
        lazy: async () => {
          const { ProfilePage } = await recoverableImport(() => import("@/pages/profile"));

          return {
            Component: ProfilePage,
          };
        },
      },
    ],
  },
  {
    path: "*",
    element: <Navigate to="/" replace />,
  },
]);
