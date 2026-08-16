import type { ReactNode } from "react";

import { hasSuperuserAccess } from "@/entities/session";

import { RouteGate } from "./RouteGate";

export function SuperuserRoute({ children }: { children: ReactNode }) {
  return (
    <RouteGate allow={hasSuperuserAccess} redirectTo="/">
      {children}
    </RouteGate>
  );
}
