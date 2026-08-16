import "@/app/styles/index.css";
import "@/app/styles/mobile-compatibility.css";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { App as AntdApp } from "antd";
import axios, { AxiosError } from "axios";
import { MemoryRouter, Route, Routes } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import { userEvent } from "vitest/browser";
import { render } from "vitest-browser-react";

import { authApi, AuthContext, type AuthContextValue, authStore, savedClientStorage } from "@/entities/session";
import { configureHttpSession, getApiErrorMessage, http, restoreAccessToken } from "@/shared/api";
import { ClientPortalThemeProvider } from "@/shared/config";

import { PortalAccessPage } from "./PortalAccessPage";

const legacyRefreshTokenKey = "melodytrack.refreshToken";

afterEach(() => {
  vi.restoreAllMocks();
  authStore.clear();
  localStorage.clear();
  document.cookie = "MelodyTrack.Csrf=; Max-Age=0; Path=/";
});

describe("browser session migration and failures", () => {
  it("exchanges a legacy refresh token once and removes it from browser storage", async () => {
    localStorage.setItem(legacyRefreshTokenKey, "legacy-refresh-secret");
    configureHttpSession(authStore);
    const post = vi.spyOn(axios, "post").mockResolvedValue({ data: { accessToken: "memory-access-token" } });

    await expect(restoreAccessToken()).resolves.toBe("memory-access-token");

    expect(post).toHaveBeenCalledWith(
      expect.stringContaining("/auth/refresh"),
      { refreshToken: "legacy-refresh-secret" },
      expect.objectContaining({ withCredentials: true }),
    );
    expect(localStorage.getItem(legacyRefreshTokenKey)).toBeNull();
    expect(authStore.getAccessToken()).toBe("memory-access-token");
    expect(Object.values(localStorage)).not.toContain("memory-access-token");
  });

  it("reports an offline request as a network failure before transport", async () => {
    vi.spyOn(window.navigator, "onLine", "get").mockReturnValue(false);
    const adapter = vi.fn();

    const error = await http.get("/protected", { adapter }).catch((reason: unknown) => reason);

    expect(error).toBeInstanceOf(AxiosError);
    expect(adapter).not.toHaveBeenCalled();
    expect(getApiErrorMessage(error)).toContain("Не удалось подключиться к серверу");
  });
});

describe("client portal access in a browser", () => {
  it("authenticates an existing PIN and establishes the portal session", async () => {
    vi.spyOn(authApi, "getClientPortalLinkStatus").mockResolvedValue({ firstName: "Анна", hasPin: true });
    const authenticate = vi.spyOn(authApi, "authenticateClientPortalLink").mockResolvedValue({
      accessToken: "portal-access-token",
      firstName: "Анна",
      lastName: "Клиент",
      savedIdentity: {
        identityId: "client-1",
        reference: "saved-reference",
        displayLabel: "Анна К.",
        lastUsedAtUtc: "2026-07-31T12:00:00.000Z",
      },
    });
    const establishSession = vi.fn().mockResolvedValue(portalUser);
    const screen = await renderPortal(establishSession);

    await expect.element(screen.getByText("Анна, введите PIN-код")).toBeVisible();
    await screen.getByLabelText("OTP Input 1", { exact: true }).click();
    await userEvent.keyboard("1234");
    await screen.getByRole("button", { name: "Войти" }).click();

    await expect.poll(() => authenticate).toHaveBeenCalledWith({ token: "link-token", pin: "1234" });
    await expect.poll(() => establishSession).toHaveBeenCalledWith("portal-access-token");
    expect(savedClientStorage.read()).toHaveLength(1);
  });

  it("shows a useful failure state when the portal link check cannot reach the API", async () => {
    vi.spyOn(authApi, "getClientPortalLinkStatus").mockRejectedValue(new AxiosError("Network Error", AxiosError.ERR_NETWORK));
    const screen = await renderPortal(vi.fn());

    await expect.element(screen.getByText("Ссылка входа недействительна")).toBeVisible();
    await expect.element(screen.getByText(/Не удалось подключиться к серверу/)).toBeVisible();
  });

  it("shows saved clients on tokenless access and signs in with the selected PIN", async () => {
    savedClientStorage.remember({
      identityId: "client-1",
      reference: "saved-reference",
      displayLabel: "Анна К.",
      lastUsedAtUtc: "2026-07-31T12:00:00.000Z",
    });
    vi.spyOn(authApi, "getSavedClientPortalStatus").mockResolvedValue({ displayLabel: "Анна К." });
    const authenticate = vi.spyOn(authApi, "authenticateSavedClientPortalIdentity").mockResolvedValue({
      accessToken: "saved-access-token",
      firstName: "Анна",
      lastName: "Клиент",
      savedIdentity: {
        identityId: "client-1",
        reference: "saved-reference",
        displayLabel: "Анна К.",
        lastUsedAtUtc: "2026-07-31T13:00:00.000Z",
      },
    });
    const establishSession = vi.fn().mockResolvedValue(portalUser);
    const screen = await renderPortal(establishSession, "/portal/access");

    await screen.getByRole("button", { name: "Анна К." }).click();
    await expect.element(screen.getByText("Анна К., введите PIN-код")).toBeVisible();
    await screen.getByLabelText("OTP Input 1", { exact: true }).click();
    await userEvent.keyboard("1234");
    await screen.getByRole("button", { name: "Войти" }).click();

    await expect.poll(() => authenticate).toHaveBeenCalledWith({ reference: "saved-reference", pin: "1234" });
    await expect.poll(() => establishSession).toHaveBeenCalledWith("saved-access-token");
  });

  it("offers an actionable empty state and removes a stale identity", async () => {
    const emptyScreen = await renderPortal(vi.fn(), "/portal/access");
    await expect.element(emptyScreen.getByText("Сохраненных сессий нет")).toBeVisible();
    await emptyScreen.unmount();

    savedClientStorage.remember({
      identityId: "client-1",
      reference: "stale-reference",
      displayLabel: "Анна К.",
      lastUsedAtUtc: "2026-07-31T12:00:00.000Z",
    });
    vi.spyOn(authApi, "getSavedClientPortalStatus").mockRejectedValue(
      new AxiosError("Forbidden", undefined, undefined, undefined, { status: 403 } as never),
    );
    const staleScreen = await renderPortal(vi.fn(), "/portal/access");
    await staleScreen.getByRole("button", { name: "Анна К." }).click();
    await expect.element(staleScreen.getByText("Профиль больше недоступен")).toBeVisible();
    await staleScreen.getByRole("button", { name: "Забыть профиль" }).click();
    expect(savedClientStorage.read()).toEqual([]);
  });
});

const portalUser = {
  id: "portal-user",
  email: "portal@example.test",
  firstName: "Анна",
  lastName: "Клиент",
  roleDisplayName: "Клиент",
  isAdmin: false,
  isSuperuser: false,
  isClientPortal: true,
  linkedClientId: "client-1",
  isTwoFactorEnabled: false,
  isTwoFactorRequired: false,
};

async function renderPortal(establishSession: AuthContextValue["establishSession"], initialEntry = "/portal/access/link-token") {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const auth: AuthContextValue = {
    isLoading: false,
    isAuthenticated: false,
    user: null,
    login: vi.fn(),
    establishSession,
    logout: vi.fn(),
  };

  return render(
    <QueryClientProvider client={queryClient}>
      <AntdApp>
        <AuthContext.Provider value={auth}>
          <ClientPortalThemeProvider>
            <MemoryRouter initialEntries={[initialEntry]}>
              <Routes>
                <Route path="/portal/access" element={<PortalAccessPage />} />
                <Route path="/portal/access/:token" element={<PortalAccessPage />} />
                <Route path="/portal" element={<div>Портал открыт</div>} />
              </Routes>
            </MemoryRouter>
          </ClientPortalThemeProvider>
        </AuthContext.Provider>
      </AntdApp>
    </QueryClientProvider>,
  );
}
