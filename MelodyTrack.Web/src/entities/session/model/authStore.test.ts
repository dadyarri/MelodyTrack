import { beforeEach, describe, expect, it, vi } from "vitest";

import { authStore } from "./authStore";

describe("authStore", () => {
  beforeEach(() => {
    authStore.clear();
    localStorage.clear();
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

  it("removes a legacy persisted access token and does not treat it as a session", () => {
    localStorage.setItem("melodytrack.accessToken", "access");
    localStorage.setItem("melodytrack.portalClients", JSON.stringify([{ token: "secret" }]));

    expect(authStore.hasSession()).toBe(false);
    authStore.clear();
    expect(localStorage.getItem("melodytrack.accessToken")).toBeNull();
    expect(localStorage.getItem("melodytrack.portalClients")).toBeNull();
  });

  it("retains approved saved-client chooser metadata when the active session is cleared", () => {
    localStorage.setItem("melodytrack.savedClientIdentities", '{"version":1,"identities":[]}');
    authStore.setSession("access");

    authStore.clear();

    expect(localStorage.getItem("melodytrack.savedClientIdentities")).toBe('{"version":1,"identities":[]}');
  });

  it("discards a legacy refresh token and observes logout performed by another tab", () => {
    const listener = vi.fn();
    const unsubscribe = authStore.subscribe(listener);
    localStorage.setItem("melodytrack.refreshToken", "legacy-refresh");
    expect(authStore.hasSession()).toBe(false);

    authStore.setSession("access");
    expect(localStorage.getItem("melodytrack.refreshToken")).toBeNull();
    listener.mockClear();

    localStorage.removeItem("melodytrack.hasSession");
    window.dispatchEvent(new StorageEvent("storage", { key: "melodytrack.hasSession", newValue: null }));

    expect(authStore.getAccessToken()).toBeNull();
    expect(authStore.hasSession()).toBe(false);
    expect(listener).toHaveBeenLastCalledWith({ hasSession: false, source: "external" });
    unsubscribe();
  });
});
