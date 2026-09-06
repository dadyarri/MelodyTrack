import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { setTestCookie } from "@/test/cookie";

import { authStore, initializeBrowserSession } from "./authStore";

describe("authStore", () => {
  beforeEach(() => {
    authStore.clear();
    localStorage.clear();
  });

  afterEach(() => {
    setTestCookie("MelodyTrack.Csrf=; Max-Age=0; Path=/");
    authStore.clear();
    localStorage.clear();
  });

  it("restores a fresh browser from the session cookie without persisting credentials", () => {
    setTestCookie("MelodyTrack.Csrf=csrf-secret; Path=/");
    localStorage.setItem("melodytrack.savedClientIdentities", '{"version":1,"identities":[]}');

    initializeBrowserSession();

    expect(authStore.hasSession()).toBe(true);
    expect(authStore.getAccessToken()).toBeNull();
    expect(localStorage.getItem("melodytrack.savedClientIdentities")).toBeNull();
    expect(Object.values(localStorage)).not.toContain("csrf-secret");
  });

  it("keeps the access token in memory and persists only a non-sensitive session marker", () => {
    authStore.setSession("access");
    authStore.setUserId("user-1");

    expect(authStore.getAccessToken()).toBe("access");
    expect(authStore.getUserId()).toBe("user-1");
    expect(authStore.hasSession()).toBe(true);
    expect(localStorage.getItem("melodytrack.accessToken")).toBeNull();
    expect(localStorage.getItem("melodytrack.refreshToken")).toBeNull();
    expect(localStorage.getItem("melodytrack.hasSession")).toBe("1");

    authStore.clear();

    expect(authStore.getAccessToken()).toBeNull();
    expect(authStore.getUserId()).toBeNull();
    expect(authStore.hasSession()).toBe(false);
  });

  it("observes logout performed by another tab", () => {
    const listener = vi.fn();
    const unsubscribe = authStore.subscribe(listener);

    authStore.setSession("access");
    listener.mockClear();

    localStorage.removeItem("melodytrack.hasSession");
    window.dispatchEvent(new StorageEvent("storage", { key: "melodytrack.hasSession", newValue: null }));

    expect(authStore.getAccessToken()).toBeNull();
    expect(authStore.hasSession()).toBe(false);
    expect(listener).toHaveBeenLastCalledWith({ hasSession: false, source: "external" });
    unsubscribe();
  });
});
