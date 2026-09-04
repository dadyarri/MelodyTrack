import type { ReactNode } from "react";

import { hasClientPortalAccess } from "@/entities/session";

import { RouteGate } from "./RouteGate";

export function ClientPortalRoute({ children }: { children: ReactNode }) {
  return (
    <RouteGate allow={(user) => hasClientPortalAccess(user)} redirectTo="/portal/access" preserveFrom>
      {children}
    </RouteGate>
  );
}
