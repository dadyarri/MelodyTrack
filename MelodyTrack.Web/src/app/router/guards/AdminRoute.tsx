import type { ReactNode } from "react";

import { hasAdminAccess } from "@/entities/session";

import { RouteGate } from "./RouteGate";

export function AdminRoute({ children }: { children: ReactNode }) {
  return (
    <RouteGate allow={hasAdminAccess} redirectTo="/">
      {children}
    </RouteGate>
  );
}
