import { type ReactNode, useCallback, useEffect, useMemo, useState } from "react";

import { PortalAppearanceContext } from "./PortalAppearanceContext";
import { parsePortalAppearance, type PortalAppearance, portalAppearanceStorageKey, readPortalAppearance, savePortalAppearance } from "./storage";

export function PortalAppearanceProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState(readPortalAppearance);
  const setAppearance = useCallback((appearance: PortalAppearance) => {
    const saved = savePortalAppearance(appearance);
    setState({ appearance, warning: saved ? "" : "Не удалось сохранить оформление. Выбор действует только на этой странице." });
  }, []);

  useEffect(() => {
    const synchronize = (event: StorageEvent) => {
      if (event.key === portalAppearanceStorageKey || event.key === null) {
        setState({ appearance: parsePortalAppearance(event.newValue), warning: "" });
      }
    };
    window.addEventListener("storage", synchronize);
    return () => { window.removeEventListener("storage", synchronize); };
  }, []);

  const value = useMemo(() => ({ ...state, setAppearance }), [state, setAppearance]);
  return <PortalAppearanceContext.Provider value={value}>{children}</PortalAppearanceContext.Provider>;
}
