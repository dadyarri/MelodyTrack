const legacyRefreshTokenKey = "melodytrack.refreshToken";
const sessionMarkerKey = "melodytrack.hasSession";
const legacyAccessTokenKey = "melodytrack.accessToken";
const legacyPortalClientsKey = "melodytrack.portalClients";

let accessToken: string | null = null;
let currentUserId: string | null = null;
type SessionChange = {
  hasSession: boolean;
  source: "local" | "external";
};
const listeners = new Set<(change: SessionChange) => void>();

if (typeof window !== "undefined") {
  // Access tokens from older builds must not remain readable by injected scripts.
  window.localStorage.removeItem(legacyAccessTokenKey);
  // Refresh credentials are cookie-only; old request-body credentials are discarded.
  window.localStorage.removeItem(legacyRefreshTokenKey);
  // Portal-link tokens are login capabilities and must never remain in browser persistence.
  window.localStorage.removeItem(legacyPortalClientsKey);
  window.addEventListener("storage", (event) => {
    if (event.key === sessionMarkerKey || event.key === null) {
      accessToken = null;
      currentUserId = null;
      notifyListeners("external");
    }
  });
}

export const authStore = {
  getAccessToken() {
    return accessToken;
  },
  getUserId() {
    return currentUserId;
  },
  hasSession() {
    return localStorage.getItem(sessionMarkerKey) === "1";
  },
  setSession(accessToken: string) {
    setAccessToken(accessToken);
    localStorage.setItem(sessionMarkerKey, "1");
    localStorage.removeItem(legacyRefreshTokenKey);
    notifyListeners("local");
  },
  setAccessToken(accessToken: string) {
    setAccessToken(accessToken);
    localStorage.setItem(sessionMarkerKey, "1");
  },
  setUserId(userId: string) {
    currentUserId = userId;
  },
  clear() {
    accessToken = null;
    currentUserId = null;
    localStorage.removeItem(sessionMarkerKey);
    localStorage.removeItem(legacyRefreshTokenKey);
    localStorage.removeItem(legacyAccessTokenKey);
    localStorage.removeItem(legacyPortalClientsKey);
    notifyListeners("local");
  },
  subscribe(listener: (change: SessionChange) => void) {
    listeners.add(listener);
    return () => {
      listeners.delete(listener);
    };
  },
};

function setAccessToken(token: string) {
  accessToken = token;
}

function notifyListeners(source: SessionChange["source"]) {
  const change = { hasSession: authStore.hasSession(), source };
  for (const listener of listeners) {
    listener(change);
  }
}
