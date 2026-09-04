import type { ReactNode } from "react";

import { hasClientPortalAccess } from "@/entities/session";

import { RouteGate } from "./RouteGate";

export function ProtectedRoute({ children }: { children: ReactNode }) {
  return (
    <RouteGate allow={(user) => Boolean(user) && !hasClientPortalAccess(user)} redirectTo="/login" preserveFrom>
      {children}
    </RouteGate>
  );
}
