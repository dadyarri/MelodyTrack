import { createContext, useContext } from "react";

import type { PortalAppearance } from "./storage";

export const PortalAppearanceContext = createContext<{
  appearance: PortalAppearance;
  warning: string;
  setAppearance: (appearance: PortalAppearance) => void;
} | null>(null);

export function usePortalAppearance() {
  const context = useContext(PortalAppearanceContext);
  if (!context) throw new Error("Portal appearance requires PortalAppearanceProvider");
  return context;
}
