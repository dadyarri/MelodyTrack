import { ConfigProvider, theme } from "antd";
import ruRU from "antd/locale/ru_RU";
import { lazy, Suspense, useEffect, useState } from "react";
import { BrowserRouter, Navigate, Route, Routes } from "react-router";
import { StaffSessionProvider, useStaffSession } from "@/entities/session";
import { SignInPage } from "@/pages/sign-in";
import { type Appearance, readAppearance, saveAppearance } from "@/shared/lib/appearance";

const AuthenticatedApplication = lazy(() => import("./AuthenticatedApplication"));

export function App() {
  const [appearance, setAppearance] = useState<Appearance>(readAppearance);

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
        algorithm: appearance === "dark" ? theme.darkAlgorithm : theme.defaultAlgorithm,
        cssVar: { prefix: "mt" },
        token: {
          colorPrimary: "#4055cf",
          borderRadius: 10,
          fontFamily: "Inter, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif",
          fontSize: 15,
          controlHeight: 44,
          colorSuccess: "#24836e",
        },
      }}
    >
      <div className="application-root">
        <BrowserRouter>
          <StaffSessionProvider>
            <ApplicationRoutes appearance={appearance} onToggleAppearance={toggleAppearance} />
          </StaffSessionProvider>
        </BrowserRouter>
      </div>
    </ConfigProvider>
  );
}

function ApplicationRoutes(props: { appearance: Appearance; onToggleAppearance: () => void }) {
  const { staff } = useStaffSession();

  if (!staff) {
    return (
      <Routes>
        <Route path="/sign-in" element={<SignInPage {...props} />} />
        <Route path="*" element={<Navigate to="/sign-in" replace />} />
      </Routes>
    );
  }

  return (
    <Suspense
      fallback={
        <div className="employee-training-loading" role="status">
          Открываем рабочее пространство…
        </div>
      }
    >
      <AuthenticatedApplication staffId={staff.staffId} {...props} />
    </Suspense>
  );
}
