import { Spin } from "antd";
import type { ReactNode } from "react";
import { Navigate, useLocation } from "react-router";

import { useAuth } from "@/entities/session";

type RouteGateProps = {
  children: ReactNode;
  allow: (user: ReturnType<typeof useAuth>["user"]) => boolean;
  redirectTo: string;
  preserveFrom?: boolean;
};

export function RouteGate({ children, allow, redirectTo, preserveFrom = false }: RouteGateProps) {
  const auth = useAuth();
  const location = useLocation();

  if (auth.isLoading) {
    return <Spin fullscreen />;
  }

  if (!allow(auth.user)) {
    return <Navigate to={redirectTo} state={preserveFrom ? { from: location } : undefined} replace />;
  }

  return children;
}
