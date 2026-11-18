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
import { PaperButton } from "@/shared/ui/paper";
import { usePopupScrollBoundary } from "@/shared/ui/scroll-boundary";
import styles from "./App.module.css";
import { useSignInCompletion } from "./useSignInCompletion";

const AuthenticatedApplication = lazy(() => import("./AuthenticatedApplication"));
const StaffRegistrationPage = lazy(() =>
  import("@/pages/registration").then((module) => ({ default: module.StaffRegistrationPage })),
);

export function App({ invitation = null }: { invitation?: string | null }) {
  usePopupScrollBoundary();
  const [appearance, setAppearance] = useState<Appearance>(readAppearance);

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
    <div className={styles.root}>
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
  const finishingSignIn = useSignInCompletion(staff?.staffId ?? null, status);
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
              <div className={styles.loading} role="status">
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
            <div className={styles.loading} role="status">
              {error ? (
                <PaperButton onClick={() => void restoreSession()}>
                  Повторить восстановление сессии
                </PaperButton>
              ) : (
                "Восстановление сессии…"
              )}
            </div>
          ) : staff && !finishingSignIn ? (
            <Suspense
              fallback={
                <div className={styles.loading} role="status">
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
