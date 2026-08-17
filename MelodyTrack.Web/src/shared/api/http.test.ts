import axios, { AxiosError, type AxiosRequestConfig, type InternalAxiosRequestConfig } from "axios";
import { afterEach, describe, expect, it, vi } from "vitest";

import { setTestCookie } from "@/test/cookie";

import {
  authExpiredEventName,
  configureHttpSession,
  discardLegacyHttpCache,
  getApiErrorMessages,
  getApiFieldErrors,
  http,
  restoreAccessToken,
} from "./index";

describe("shared HTTP transport", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    setTestCookie("MelodyTrack.Csrf=; Max-Age=0; Path=/");
  });

  it("gets authentication from an injected session adapter", async () => {
    let capturedConfig: InternalAxiosRequestConfig | undefined;
    configureHttpSession({
      clear: vi.fn(),
      clearLegacyRefreshToken: vi.fn(),
      getAccessToken: () => "access-token",
      getLegacyRefreshToken: () => null,
      setAccessToken: vi.fn(),
    });

    await http.get("/test", {
      adapter: (config) => {
        capturedConfig = config;
        return Promise.resolve({
          config,
          data: null,
          headers: {},
          status: 200,
          statusText: "OK",
        });
      },
    });

    expect(capturedConfig?.headers.Authorization).toBe("Bearer access-token");
  });

  it("adds the CSRF cookie value only to state-changing requests", async () => {
    setTestCookie("MelodyTrack.Csrf=csrf-token; Path=/");
    const capturedConfigs: InternalAxiosRequestConfig[] = [];
    const adapter = (config: InternalAxiosRequestConfig) => {
      capturedConfigs.push(config);
      return Promise.resolve({ config, data: null, headers: {}, status: 200, statusText: "OK" });
    };

    await http.get("/read", { adapter });
    await http.post("/write", {}, { adapter });

    expect(capturedConfigs[0]?.headers["X-CSRF-Token"]).toBeUndefined();
    expect(capturedConfigs[1]?.headers["X-CSRF-Token"]).toBe("csrf-token");
  });

  it("normalizes non-HTTP errors", () => {
    expect(getApiErrorMessages(new Error("Something failed"))).toEqual(["Something failed"]);
    expect(getApiErrorMessages("unknown")).toEqual(["Произошла неизвестная ошибка"]);
  });

  it("parses only the canonical Problem Details contract", () => {
    const config = {} as InternalAxiosRequestConfig;
    const canonical = new AxiosError("Bad Request", "ERR_BAD_REQUEST", config, undefined, {
      config,
      data: {
        type: "urn:melody-track:problem:validation",
        title: "Request validation failed",
        status: 400,
        detail: "Correct the highlighted fields.",
        instance: "/test",
        code: "validation_failed",
        traceId: "trace-1",
        errors: [{ path: "Pin", code: "NotEmptyValidator", message: "Введите PIN-код" }],
      },
      headers: {},
      status: 400,
      statusText: "Bad Request",
    });
    const legacy = new AxiosError("Bad Request", "ERR_BAD_REQUEST", config, undefined, {
      config,
      data: { message: "legacy error", errors: { pin: ["legacy field error"] } },
      headers: {},
      status: 400,
      statusText: "Bad Request",
    });

    expect(getApiErrorMessages(canonical)).toEqual(["Введите PIN-код\nCorrect the highlighted fields."]);
    expect(getApiFieldErrors(canonical)).toEqual({ pin: ["Введите PIN-код"] });
    expect(getApiErrorMessages(legacy)).toEqual(["Сервер не смог обработать запрос (HTTP 400)."]);
    expect(getApiFieldErrors(legacy)).toEqual({});
  });

  it("refreshes an expired access token and retries the original request once", async () => {
    let accessToken = "expired-access";
    const setAccessToken = vi.fn((nextAccessToken: string) => {
      accessToken = nextAccessToken;
    });
    const clearLegacyRefreshToken = vi.fn();
    configureHttpSession({
      clear: vi.fn(),
      clearLegacyRefreshToken,
      getAccessToken: () => accessToken,
      getLegacyRefreshToken: () => null,
      setAccessToken,
    });
    vi.spyOn(axios, "post").mockResolvedValue({
      data: { accessToken: "fresh-access" },
    });
    let attempt = 0;
    let retriedAuthorization: unknown;

    await http.get("/protected", {
      adapter: (config) => {
        attempt += 1;
        if (attempt === 1) {
          return Promise.reject(
            new AxiosError("Unauthorized", "ERR_BAD_REQUEST", config, undefined, {
              config,
              data: null,
              headers: {},
              status: 401,
              statusText: "Unauthorized",
            }),
          );
        }
        retriedAuthorization = config.headers.Authorization;
        return Promise.resolve({
          config,
          data: { ok: true },
          headers: {},
          status: 200,
          statusText: "OK",
        });
      },
    });

    expect(setAccessToken).toHaveBeenCalledWith("fresh-access");
    expect(clearLegacyRefreshToken).toHaveBeenCalledOnce();
    expect(retriedAuthorization).toBe("Bearer fresh-access");
    expect(attempt).toBe(2);
  });

  it("restores an access token before the first authenticated request", async () => {
    let accessToken: string | null = null;
    const setAccessToken = vi.fn((nextAccessToken: string) => {
      accessToken = nextAccessToken;
    });
    const clearLegacyRefreshToken = vi.fn();
    configureHttpSession({
      clear: vi.fn(),
      clearLegacyRefreshToken,
      getAccessToken: () => accessToken,
      getLegacyRefreshToken: () => "refresh-1",
      setAccessToken,
    });
    vi.spyOn(axios, "post").mockResolvedValue({
      data: { accessToken: "fresh-access" },
    });

    await expect(restoreAccessToken()).resolves.toBe("fresh-access");
    expect(setAccessToken).toHaveBeenCalledWith("fresh-access");
    expect(clearLegacyRefreshToken).toHaveBeenCalledOnce();
  });

  it("refreshes a cookie session with credentials and explicit CSRF protection", async () => {
    setTestCookie("MelodyTrack.Csrf=cookie-csrf; Path=/");
    configureHttpSession({
      clear: vi.fn(),
      clearLegacyRefreshToken: vi.fn(),
      getAccessToken: () => null,
      getLegacyRefreshToken: () => null,
      setAccessToken: vi.fn(),
    });
    const post = vi.spyOn(axios, "post").mockResolvedValue({ data: { accessToken: "fresh-access" } });

    await expect(restoreAccessToken()).resolves.toBe("fresh-access");

    expect(post).toHaveBeenCalledOnce();
    const [url, body, config] = post.mock.calls[0] as [string, unknown, AxiosRequestConfig];
    expect(url).toContain("/auth/refresh");
    expect(body).toEqual({});
    expect(config.withCredentials).toBe(true);
    expect(config.headers).toMatchObject({ "X-CSRF-Token": "cookie-csrf" });
  });

  it("clears and publishes expiry when the refresh session is invalid", async () => {
    const clear = vi.fn();
    configureHttpSession({
      clear,
      clearLegacyRefreshToken: vi.fn(),
      getAccessToken: () => "expired-access",
      getLegacyRefreshToken: () => null,
      setAccessToken: vi.fn(),
    });
    const refreshConfig = {} as InternalAxiosRequestConfig;
    vi.spyOn(axios, "post").mockRejectedValue(
      new AxiosError("Unauthorized", "ERR_BAD_REQUEST", refreshConfig, undefined, {
        config: refreshConfig,
        data: null,
        headers: {},
        status: 401,
        statusText: "Unauthorized",
      }),
    );
    const expiredListener = vi.fn();
    window.addEventListener(authExpiredEventName, expiredListener);

    await expect(
      http.get("/protected", {
        adapter: (config) =>
          Promise.reject(
            new AxiosError("Unauthorized", "ERR_BAD_REQUEST", config, undefined, {
              config,
              data: null,
              headers: {},
              status: 401,
              statusText: "Unauthorized",
            }),
          ),
      }),
    ).rejects.toThrow("Сессия истекла");

    expect(clear).toHaveBeenCalledOnce();
    expect(expiredListener).toHaveBeenCalledOnce();
    window.removeEventListener(authExpiredEventName, expiredListener);
  });

  it("keeps the session when refreshing fails transiently", async () => {
    const clear = vi.fn();
    configureHttpSession({
      clear,
      clearLegacyRefreshToken: vi.fn(),
      getAccessToken: () => "expired-access",
      getLegacyRefreshToken: () => null,
      setAccessToken: vi.fn(),
    });
    vi.spyOn(axios, "post").mockRejectedValue(new AxiosError("Network unavailable", AxiosError.ERR_NETWORK));
    const expiredListener = vi.fn();
    window.addEventListener(authExpiredEventName, expiredListener);

    await expect(
      http.get("/protected", {
        adapter: (config) =>
          Promise.reject(
            new AxiosError("Unauthorized", "ERR_BAD_REQUEST", config, undefined, {
              config,
              data: null,
              headers: {},
              status: 401,
              statusText: "Unauthorized",
            }),
          ),
      }),
    ).rejects.toThrow("Не удалось обновить сессию");

    expect(clear).not.toHaveBeenCalled();
    expect(expiredListener).not.toHaveBeenCalled();
    window.removeEventListener(authExpiredEventName, expiredListener);
  });

  it("recovers when the first refresh attempt fails transiently", async () => {
    const setAccessToken = vi.fn();
    configureHttpSession({
      clear: vi.fn(),
      clearLegacyRefreshToken: vi.fn(),
      getAccessToken: () => null,
      getLegacyRefreshToken: () => null,
      setAccessToken,
    });
    const post = vi
      .spyOn(axios, "post")
      .mockRejectedValueOnce(new AxiosError("Network unavailable", AxiosError.ERR_NETWORK))
      .mockResolvedValueOnce({ data: { accessToken: "fresh-access" } });

    await expect(restoreAccessToken()).resolves.toBe("fresh-access");

    expect(post).toHaveBeenCalledTimes(2);
    expect(setAccessToken).toHaveBeenCalledWith("fresh-access");
  });

  it("retries refresh when another request rotates the cookie", async () => {
    setTestCookie("MelodyTrack.Csrf=csrf-before; Path=/");
    const setAccessToken = vi.fn();
    configureHttpSession({
      clear: vi.fn(),
      clearLegacyRefreshToken: vi.fn(),
      getAccessToken: () => null,
      getLegacyRefreshToken: () => null,
      setAccessToken,
    });
    const forbiddenConfig = {} as InternalAxiosRequestConfig;
    const post = vi.spyOn(axios, "post").mockImplementationOnce(() => {
      setTestCookie("MelodyTrack.Csrf=csrf-after; Path=/");
      return Promise.reject(
        new AxiosError("Forbidden", "ERR_BAD_REQUEST", forbiddenConfig, undefined, {
          config: forbiddenConfig,
          data: null,
          headers: {},
          status: 403,
          statusText: "Forbidden",
        }),
      );
    });
    post.mockResolvedValueOnce({ data: { accessToken: "fresh-access" } });

    await expect(restoreAccessToken()).resolves.toBe("fresh-access");

    expect(post).toHaveBeenCalledTimes(2);
    expect(post.mock.calls[0]?.[2]?.headers).toMatchObject({ "X-CSRF-Token": "csrf-before" });
    expect(post.mock.calls[1]?.[2]?.headers).toMatchObject({ "X-CSRF-Token": "csrf-after" });
    expect(setAccessToken).toHaveBeenCalledWith("fresh-access");
  });

  it("removes only generic legacy response-cache entries", () => {
    localStorage.setItem("melodytrack:http-cache:get:/client-portal/auth/link/secret-token", "{}");
    localStorage.setItem("melodytrack.theme", "dark");

    discardLegacyHttpCache(localStorage);

    expect(localStorage.getItem("melodytrack:http-cache:get:/client-portal/auth/link/secret-token")).toBeNull();
    expect(localStorage.getItem("melodytrack.theme")).toBe("dark");
  });
});
