const sessionMarkerKey = "melodytrack.hasSession";

let accessToken: string | null = null;
let currentUserId: string | null = null;
type SessionChange = {
  hasSession: boolean;
  source: "local" | "external";
};
const listeners = new Set<(change: SessionChange) => void>();

if (typeof window !== "undefined") {
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
