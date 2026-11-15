import { afterEach, describe, expect, it, vi } from "vitest";
import { readAppearance, saveAppearance } from "./appearance";

afterEach(() => {
  localStorage.clear();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("appearance preference", () => {
  it.each(["light", "dark"] as const)(
    "restores an explicit %s preference before the OS setting",
    (appearance) => {
      saveAppearance(appearance);
      expect(readAppearance()).toBe(appearance);
    },
  );

  it("uses the OS preference when storage is unavailable and keeps writes recoverable", () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new DOMException("Denied", "SecurityError");
    });
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new DOMException("Denied", "SecurityError");
    });
    vi.stubGlobal("matchMedia", vi.fn().mockReturnValue({ matches: true }));
    expect(readAppearance()).toBe("dark");
    expect(() => saveAppearance("light")).not.toThrow();
  });

  it("rejects corrupt stored preferences and uses the OS fallback", () => {
    localStorage.setItem("melodytrack.rewrite.appearance", "unsupported");
    vi.stubGlobal("matchMedia", vi.fn().mockReturnValue({ matches: false }));
    expect(readAppearance()).toBe("light");
  });
});
