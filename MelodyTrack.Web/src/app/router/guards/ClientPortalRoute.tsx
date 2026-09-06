import { Spin } from "antd";
import type { ReactNode } from "react";
import { Navigate } from "react-router";

import { hasClientPortalAccess, useAuth } from "@/entities/session";

export function ClientPortalRoute({ children }: { children: ReactNode }) {
  const auth = useAuth();
  if (auth.isLoading) return <Spin fullscreen />;
  if (!hasClientPortalAccess(auth.user)) return <Navigate to="/portal/error?reason=session" replace />;
  if (!auth.user?.portalAlias) return <Navigate to="/portal/alias" replace />;
  return children;
}

export function ClientPortalAliasRoute({ children }: { children: ReactNode }) {
  const auth = useAuth();
  if (auth.isLoading) return <Spin fullscreen />;
  if (!hasClientPortalAccess(auth.user)) return <Navigate to="/portal/error?reason=session" replace />;
  if (auth.user?.portalAlias) return <Navigate to="/portal" replace />;
  return children;
}
