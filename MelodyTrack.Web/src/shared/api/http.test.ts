import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { setTestCookie } from "@/test/cookie";

import {
  AppError,
  authExpiredEventName,
  configureHttpSession,
  getApiErrorMessage,
  getApiFieldErrors,
  http,
  isHttpRequestCanceled,
  restoreAccessToken,
} from "./http";

describe("Kiota HTTP session transport", () => {
  const clear = vi.fn();
  let accessToken: string | null;

  beforeEach(() => {
    accessToken = "access-before";
    clear.mockClear();
    configureHttpSession({
      clear,
      getAccessToken: () => accessToken,
      hasSession: () => true,
      setAccessToken: (token) => {
        accessToken = token;
      },
    });
    setTestCookie("MelodyTrack.Csrf=; Max-Age=0; Path=/");
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("preserves bearer, idempotency, cancellation, and cookie credentials", async () => {
    const controller = new AbortController();
    const fetchMock = vi.spyOn(globalThis, "fetch").mockResolvedValue(jsonResponse({ id: "created" }));

    await http.post(
      "/clients",
      { name: "Client" },
      {
        headers: { "Idempotency-Key": "request-1" },
        signal: controller.signal,
      },
    );

    const [, init] = fetchMock.mock.calls[0] ?? [];
    const headers = new Headers(init?.headers);
    expect(headers.get("Authorization")).toBe("Bearer access-before");
    expect(headers.get("Idempotency-Key")).toBe("request-1");
    expect(headers.has("X-CSRF-Token")).toBe(false);
    expect(init?.credentials).toBe("include");
    expect(init?.signal).toBe(controller.signal);
  });

  it("adds CSRF only to cookie-authenticated session operations", async () => {
    setTestCookie("MelodyTrack.Csrf=csrf-token; Path=/");
    accessToken = null;
    const fetchMock = vi.spyOn(globalThis, "fetch").mockResolvedValue(jsonResponse({ accessToken: "fresh-access" }));

    await restoreAccessToken();

    const [, init] = fetchMock.mock.calls[0] ?? [];
    expect(new Headers(init?.headers).get("X-CSRF-Token")).toBe("csrf-token");
  });

  it("coalesces concurrent 401 responses and replays every original request once", async () => {
    let refreshCalls = 0;
    let protectedCalls = 0;
    vi.spyOn(globalThis, "fetch").mockImplementation((url, init) => {
      if (getRequestUrl(url).endsWith("/auth/refresh")) {
        refreshCalls += 1;
        return Promise.resolve(jsonResponse({ accessToken: "access-after" }));
      }

      protectedCalls += 1;
      const token = new Headers(init?.headers).get("Authorization");
      return Promise.resolve(token === "Bearer access-after" ? jsonResponse({ ok: true }) : jsonResponse(createProblem(401), 401));
    });

    await Promise.all([http.get("/clients"), http.get("/services")]);

    expect(refreshCalls).toBe(1);
    expect(protectedCalls).toBe(4);
    expect(accessToken).toBe("access-after");
  });

  it("publishes terminal expiry once and does not replay a state-changing request", async () => {
    const onExpired = vi.fn();
    window.addEventListener(authExpiredEventName, onExpired);
    const fetchMock = vi.spyOn(globalThis, "fetch").mockImplementation(() => Promise.resolve(jsonResponse(createProblem(401), 401)));

    await expect(http.post("/payments", { amount: 100 })).rejects.toMatchObject({ status: 401 });

    expect(clear).toHaveBeenCalledOnce();
    expect(onExpired).toHaveBeenCalledOnce();
    expect(fetchMock.mock.calls.filter(([url]) => getRequestUrl(url).endsWith("/payments"))).toHaveLength(1);
    window.removeEventListener(authExpiredEventName, onExpired);
  });

  it("keeps the session when refresh fails because the network is unavailable", async () => {
    vi.spyOn(globalThis, "fetch").mockImplementation((url) => {
      if (getRequestUrl(url).endsWith("/auth/refresh")) {
        return Promise.reject(new TypeError("Network unavailable"));
      }
      return Promise.resolve(jsonResponse(createProblem(401), 401));
    });

    await expect(http.get("/clients")).rejects.toMatchObject({ kind: "network" });

    expect(clear).not.toHaveBeenCalled();
  });

  it("normalizes problem details and field errors", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      jsonResponse(
        {
          ...createProblem(400),
          detail: "Проверьте данные",
          errors: [{ path: "Email", code: "invalid", message: "Некорректная почта" }],
        },
        400,
      ),
    );

    const error = await http.post("/auth/login", {}).catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(AppError);
    expect(getApiErrorMessage(error)).toContain("Некорректная почта");
    expect(getApiFieldErrors(error)).toEqual({ email: ["Некорректная почта"] });
  });

  it("recognizes AbortSignal cancellation", async () => {
    vi.spyOn(globalThis, "fetch").mockRejectedValue(new DOMException("Aborted", "AbortError"));

    const error = await http.get("/clients").catch((caught: unknown) => caught);

    expect(isHttpRequestCanceled(error)).toBe(true);
  });
});

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function createProblem(status: number) {
  return {
    type: `urn:melody-track:problem:${String(status)}`,
    title: "Ошибка",
    status,
    instance: "/api/test",
    code: `http_${String(status)}`,
    traceId: "0123456789abcdef0123456789abcdef",
    errors: [],
  };
}

function getRequestUrl(input: RequestInfo | URL | undefined) {
  if (typeof input === "string") {
    return input;
  }
  if (input instanceof URL) {
    return input.href;
  }
  return input?.url ?? "";
}
