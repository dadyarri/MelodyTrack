export type Appearance = "light" | "dark";
const key = "melodytrack.rewrite.appearance";

export function readAppearance(): Appearance {
  try {
    const stored = localStorage.getItem(key);
    if (stored === "light" || stored === "dark") return stored;
  } catch {
    // Appearance is a non-sensitive preference; unavailable storage does not block work.
  }
  return window.matchMedia?.("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

export function saveAppearance(value: Appearance): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // The preference remains active for this tab.
  }
}
