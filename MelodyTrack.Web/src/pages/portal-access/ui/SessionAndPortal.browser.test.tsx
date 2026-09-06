import "@/app/styles/index.css";
import "@/app/styles/mobile-compatibility.css";

import { App as AntdApp } from "antd";
import { MemoryRouter } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import { render } from "vitest-browser-react";

import { authStore } from "@/entities/session";
import { AppError, configureHttpSession, getApiErrorMessage, http, restoreAccessToken } from "@/shared/api";
import { ClientPortalThemeProvider } from "@/shared/config";
import { setTestCookie } from "@/test/cookie";

import { PortalAccessPage } from "./PortalAccessPage";

afterEach(() => {
  vi.restoreAllMocks();
  authStore.clear();
  localStorage.clear();
  setTestCookie("MelodyTrack.Csrf=; Max-Age=0; Path=/");
});

describe("browser session refresh and failures", () => {
  it("uses the cookie refresh session and keeps the new access token in memory", async () => {
    configureHttpSession(authStore);
    const fetchMock = vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(JSON.stringify({ accessToken: "memory-access-token" }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
    );

    await expect(restoreAccessToken()).resolves.toBe("memory-access-token");

    expect(fetchMock).toHaveBeenCalledOnce();
    expect(getRequestUrl(fetchMock.mock.calls[0]?.[0])).toContain("/auth/refresh");
    expect(fetchMock.mock.calls[0]?.[1]?.credentials).toBe("include");
    expect(authStore.getAccessToken()).toBe("memory-access-token");
    expect(Object.values(localStorage)).not.toContain("memory-access-token");
  });

  it("recovers the first protected request after a suspended page resumes", async () => {
    authStore.setSession("expired-access-token");
    configureHttpSession(authStore);
    let protectedCalls = 0;
    let refreshCalls = 0;
    const fetchMock = vi.spyOn(globalThis, "fetch").mockImplementation((input, init) => {
      if (getRequestUrl(input).endsWith("/auth/refresh")) {
        refreshCalls += 1;
        return Promise.resolve(
          new Response(JSON.stringify({ accessToken: "resumed-access-token" }), {
            status: 200,
            headers: { "Content-Type": "application/json" },
          }),
        );
      }

      protectedCalls += 1;
      const authorization = new Headers(init?.headers).get("Authorization");
      return Promise.resolve(
        new Response(JSON.stringify(authorization === "Bearer resumed-access-token" ? { resumed: true } : authProblem), {
          status: authorization === "Bearer resumed-access-token" ? 200 : 401,
          headers: { "Content-Type": "application/json" },
        }),
      );
    });

    window.dispatchEvent(new Event("pageshow"));
    await expect(http.get<{ resumed: boolean }>("/auth/me")).resolves.toMatchObject({ data: { resumed: true } });

    expect(refreshCalls).toBe(1);
    expect(protectedCalls).toBe(2);
    expect(authStore.getAccessToken()).toBe("resumed-access-token");
    expect(fetchMock).toHaveBeenCalledTimes(3);
  });

  it("reports a fetch failure as a network failure without clearing session state", async () => {
    vi.spyOn(globalThis, "fetch").mockRejectedValue(new TypeError("Network unavailable"));

    const error = await http.get("/protected").catch((reason: unknown) => reason);

    expect(error).toBeInstanceOf(AppError);
    expect(getApiErrorMessage(error)).toContain("Не удалось подключиться к серверу");
  });
});

const authProblem = {
  type: "urn:melody-track:problem:unauthorized",
  title: "Unauthorized",
  status: 401,
  instance: "/api/auth/me",
  code: "unauthorized",
  traceId: "0123456789abcdef0123456789abcdef",
  errors: [],
};

function getRequestUrl(input: RequestInfo | URL | undefined) {
  if (typeof input === "string") {
    return input;
  }
  if (input instanceof URL) {
    return input.href;
  }
  return input?.url ?? "";
}

describe("portal access guidance", () => {
  it("explains how to replace an invalid link without offering credential entry", async () => {
    const screen = await renderPortal("/portal/error");

    await expect.element(screen.getByText("Не удалось открыть кабинет")).toBeVisible();
    await expect.element(screen.getByText(/Попросите администратора прислать новую личную ссылку/)).toBeVisible();
    await expect.element(screen.getByRole("link", { name: "Вход для сотрудников" })).toHaveAttribute("href", "/login");
    await expect.element(screen.getByRole("textbox")).not.toBeInTheDocument();
  });

  it("asks an expired session to reopen the personal link", async () => {
    const screen = await renderPortal("/portal/error?reason=session");

    await expect.element(screen.getByText("Войдите по личной ссылке")).toBeVisible();
  });

  it("explains when a throttled navigation can be retried", async () => {
    const screen = await renderPortal("/portal/error?reason=rate-limit");

    await expect.element(screen.getByText(/Подождите минуту/)).toBeVisible();
  });
});

async function renderPortal(initialEntry: string) {
  return render(
    <AntdApp>
      <ClientPortalThemeProvider>
        <MemoryRouter initialEntries={[initialEntry]}>
          <PortalAccessPage />
        </MemoryRouter>
      </ClientPortalThemeProvider>
    </AntdApp>,
  );
}
