export type PortalAppearance = "rpg" | "minimal";

export const portalAppearanceStorageKey = "melodytrack.portalAppearance";

export function parsePortalAppearance(value: string | null): PortalAppearance {
  return value === "minimal" ? "minimal" : "rpg";
}

export function readPortalAppearance() {
  try {
    return { appearance: parsePortalAppearance(localStorage.getItem(portalAppearanceStorageKey)), warning: "" };
  } catch {
    return {
      appearance: "rpg" as PortalAppearance,
      warning: "Хранилище браузера недоступно. Выбор оформления действует только на этой странице.",
    };
  }
}

export function savePortalAppearance(appearance: PortalAppearance): boolean {
  try {
    localStorage.setItem(portalAppearanceStorageKey, appearance);
    return true;
  } catch {
    return false;
  }
}
