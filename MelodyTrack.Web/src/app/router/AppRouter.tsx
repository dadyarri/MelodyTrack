import { useEffect, useState, useSyncExternalStore } from "react";
import { RouterProvider } from "react-router";

import { AppLoadingScreen } from "@/shared/ui";

import { router } from "./router";

const navigationLoadingDelayMs = 180;

function subscribeToRouter(onStoreChange: () => void) {
  return router.subscribe(onStoreChange);
}

function getRouterState() {
  return router.state;
}

export function AppRouter() {
  const routerState = useSyncExternalStore(subscribeToRouter, getRouterState, getRouterState);
  const isNavigating = routerState.initialized && routerState.navigation.state !== "idle";

  if (!routerState.initialized) {
    return <AppLoadingScreen />;
  }

  return (
    <>
      <RouterProvider router={router} />
      {isNavigating ? <DelayedNavigationLoading /> : null}
    </>
  );
}

function DelayedNavigationLoading() {
  const [isVisible, setIsVisible] = useState(false);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      setIsVisible(true);
    }, navigationLoadingDelayMs);

    return () => {
      window.clearTimeout(timer);
    };
  }, []);

  return isVisible ? <AppLoadingScreen message="Открываем раздел…" /> : null;
}
