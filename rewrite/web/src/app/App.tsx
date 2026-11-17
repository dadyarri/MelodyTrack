import { Button, ConfigProvider, theme } from "antd";
import ruRU from "antd/locale/ru_RU";
import {
  lazy,
  type ReactNode,
  Suspense,
  useCallback,
  useEffect,
  useLayoutEffect,
  useState,
} from "react";
import { BrowserRouter, Navigate, Route, Routes } from "react-router";
import { OfflineStorageProvider, useOfflineStorage } from "@/entities/offline-storage";
import { StaffSessionProvider, useStaffSession } from "@/entities/session";
import { SignInPage } from "@/pages/sign-in";
import type { StaffIdentity } from "@/shared/api";
import { type Appearance, readAppearance, saveAppearance } from "@/shared/lib/appearance";
import { Notifications, useErrorNotification } from "@/shared/ui/notifications";
import { usePopupScrollBoundary } from "@/shared/ui/scroll-boundary";

const AuthenticatedApplication = lazy(() => import("./AuthenticatedApplication"));
const StaffRegistrationPage = lazy(() =>
  import("@/pages/registration").then((module) => ({ default: module.StaffRegistrationPage })),
);

export function App({ invitation = null }: { invitation?: string | null }) {
  usePopupScrollBoundary();
  const [appearance, setAppearance] = useState<Appearance>(readAppearance);
  const dark = appearance === "dark";

  useLayoutEffect(() => {
    document.documentElement.dataset.appearance = appearance;
    document.documentElement.style.colorScheme = appearance;
    const background = appearance === "dark" ? "#15120f" : "#f4e8c8";
    document.documentElement.style.backgroundColor = background;
    document.querySelector('meta[name="theme-color"]')?.setAttribute("content", background);
    saveAppearance(appearance);
  }, [appearance]);

  const toggleAppearance = () =>
    setAppearance((current) => (current === "light" ? "dark" : "light"));

  return (
    <ConfigProvider
      locale={ruRU}
      theme={{
        algorithm: dark ? theme.darkAlgorithm : theme.defaultAlgorithm,
        cssVar: { prefix: "mt" },
        token: {
          colorPrimary: dark ? "#d0a35b" : "#8b6226",
          colorInfo: dark ? "#b78d4a" : "#6f6942",
          colorSuccess: dark ? "#7ca46d" : "#5f7b4b",
          colorWarning: dark ? "#d4a24f" : "#a87322",
          colorError: dark ? "#d27a70" : "#9b4a3c",
          colorText: dark ? "#f1e7d1" : "#33271a",
          colorTextSecondary: dark ? "#bcae94" : "#746550",
          colorBgBase: dark ? "#15120f" : "#f4e8c8",
          colorBgContainer: dark ? "#1f1914" : "#f8edcf",
          colorBgElevated: dark ? "#261f18" : "#fbf3dc",
          colorFillAlter: dark ? "#31271d" : "#efe1be",
          colorBorder: dark ? "#5d4a35" : "#d4c19a",
          colorBorderSecondary: dark ? "#463727" : "#e8dcc2",
          borderRadius: 6,
          fontFamily: "'IBM Plex Sans', 'Segoe UI', sans-serif",
          fontSize: 15,
          controlHeight: 44,
        },
        components: {
          Button: {
            primaryShadow: "none",
            defaultShadow: "none",
            dangerShadow: "none",
            defaultBg: dark ? "#261f18" : "#fbf3dc",
          },
          Input: {
            activeBg: dark ? "#261f18" : "#fbf3dc",
            hoverBg: dark ? "#261f18" : "#fbf3dc",
          },
          Modal: {
            headerBg: dark ? "#1f1914" : "#f8edcf",
            contentBg: dark ? "#1f1914" : "#f8edcf",
            footerBg: dark ? "#1f1914" : "#f8edcf",
          },
        },
      }}
    >
      <div className="application-root">
        <BrowserRouter>
          <OfflineStorageProvider>
            <SessionWithOfflineStorage>
              <ApplicationRoutes
                invitation={invitation}
                appearance={appearance}
                onToggleAppearance={toggleAppearance}
              />
            </SessionWithOfflineStorage>
          </OfflineStorageProvider>
        </BrowserRouter>
        <Notifications appearance={appearance} />
      </div>
    </ConfigProvider>
  );
}

function SessionWithOfflineStorage({ children }: { children: ReactNode }) {
  const storage = useOfflineStorage();
  const authenticate = storage?.authenticate;
  const onAuthenticated = useCallback(
    (identity: StaffIdentity, context: { source: "credential" | "restore"; password?: string }) => {
      authenticate?.(identity.staffId, context);
    },
    [authenticate],
  );
  return (
    <StaffSessionProvider
      onAuthenticated={onAuthenticated}
      onSessionCleared={() => storage?.clear()}
      onLogout={() => storage?.logout()}
    >
      <OfflineClientAccess />
      {children}
    </StaffSessionProvider>
  );
}

function OfflineClientAccess() {
  const { staff } = useStaffSession();
  const storage = useOfflineStorage();
  const revoke = storage?.revokeClientAccess;
  const canRead = staff?.permissions.some(
    (grant) =>
      grant.permission === "clientsRead" &&
      grant.allowed &&
      grant.dataScope === "available" &&
      grant.timeScope === "any",
  );
  const canWrite = staff?.permissions.some(
    (grant) =>
      grant.permission === "clientsWrite" &&
      grant.allowed &&
      grant.dataScope === "available" &&
      ["any", "today", "currentMonth"].includes(grant.timeScope),
  );
  useEffect(() => {
    if (staff && storage?.staffId === staff.staffId && (!canRead || !canWrite)) void revoke?.();
  }, [staff, storage?.staffId, canRead, canWrite, revoke]);
  return null;
}

function ApplicationRoutes(props: {
  invitation: string | null;
  appearance: Appearance;
  onToggleAppearance: () => void;
}) {
  const { staff, status, error, restoreSession } = useStaffSession();
  useErrorNotification(status === "restoring" ? error?.message : null, {
    title: "Не удалось восстановить сессию",
    retry: () => void restoreSession(),
  });

  return (
    <Routes>
      <Route
        path="/auth/register"
        element={
          <Suspense
            fallback={
              <div className="employee-training-loading" role="status">
                Загрузка…
              </div>
            }
          >
            <StaffRegistrationPage {...props} />
          </Suspense>
        }
      />
      <Route
        path="*"
        element={
          status === "restoring" ? (
            <div className="employee-training-loading" role="status">
              {error ? (
                <Button onClick={() => void restoreSession()}>
                  Повторить восстановление сессии
                </Button>
              ) : (
                "Восстановление сессии…"
              )}
            </div>
          ) : staff ? (
            <Suspense
              fallback={
                <div className="employee-training-loading" role="status">
                  Загрузка…
                </div>
              }
            >
              <AuthenticatedApplication staffId={staff.staffId} {...props} />
            </Suspense>
          ) : (
            <Routes>
              <Route path="/sign-in" element={<SignInPage {...props} />} />
              <Route path="*" element={<Navigate to="/sign-in" replace />} />
            </Routes>
          )
        }
      />
    </Routes>
  );
}
