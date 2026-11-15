import { Alert, Button, ConfigProvider, theme } from "antd";
import ruRU from "antd/locale/ru_RU";
import { lazy, Suspense, useEffect, useState } from "react";
import { BrowserRouter, Navigate, Route, Routes } from "react-router";
import { StaffSessionProvider, useStaffSession } from "@/entities/session";
import { SignInPage } from "@/pages/sign-in";
import { type Appearance, readAppearance, saveAppearance } from "@/shared/lib/appearance";
import { usePopupScrollBoundary } from "@/shared/ui/scroll-boundary";

const AuthenticatedApplication = lazy(() => import("./AuthenticatedApplication"));
const StaffRegistrationPage = lazy(() =>
  import("@/pages/registration").then((module) => ({ default: module.StaffRegistrationPage })),
);

export function App({ invitation = null }: { invitation?: string | null }) {
  usePopupScrollBoundary();
  const [appearance, setAppearance] = useState<Appearance>(readAppearance);
  const dark = appearance === "dark";

  useEffect(() => {
    document.documentElement.dataset.appearance = appearance;
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
          <StaffSessionProvider>
            <ApplicationRoutes
              invitation={invitation}
              appearance={appearance}
              onToggleAppearance={toggleAppearance}
            />
          </StaffSessionProvider>
        </BrowserRouter>
      </div>
    </ConfigProvider>
  );
}

function ApplicationRoutes(props: {
  invitation: string | null;
  appearance: Appearance;
  onToggleAppearance: () => void;
}) {
  const { staff, status, error, restoreSession } = useStaffSession();

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
                <Alert
                  type="warning"
                  title="Не удалось восстановить сессию"
                  description={error.message}
                  action={
                    <Button
                      onClick={() => {
                        void restoreSession();
                      }}
                    >
                      Повторить
                    </Button>
                  }
                />
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
