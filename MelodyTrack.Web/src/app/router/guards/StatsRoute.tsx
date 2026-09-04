import type { ReactNode } from "react";

import { hasStatsAccess } from "@/entities/session";

import { RouteGate } from "./RouteGate";

export function StatsRoute({ children }: { children: ReactNode }) {
  return (
    <RouteGate allow={hasStatsAccess} redirectTo="/">
      {children}
    </RouteGate>
  );
}
