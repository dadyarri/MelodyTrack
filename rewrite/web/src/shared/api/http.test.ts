import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError, apiOptions, invalidResponse, staffApi, toApiError } from "./http";

afterEach(() => vi.unstubAllGlobals());

describe("API transport", () => {
  it("sends credentials in a noncached body with the browser cookie", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValue(
        new Response("{}", { status: 200, headers: { "Content-Type": "application/json" } }),
      );
    vi.stubGlobal("fetch", fetch);
    const signal = new AbortController().signal;

    await staffApi().staff.password.post(
      { login: "teacher", password: "test-only" },
      apiOptions(signal),
    );

    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining("/api/auth/staff/password"),
      expect.objectContaining({
        credentials: "include",
        cache: "no-store",
        redirect: "error",
        signal,
      }),
    );
    const init = fetch.mock.calls[0]?.[1] as RequestInit;
    expect(await new Response(init.body).json()).toEqual({
      login: "teacher",
      password: "test-only",
    });
  });

  it("does not expose raw problem details or arbitrary trace text", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ detail: "private body", traceId: "private body" }), {
          status: 401,
        }),
      ),
    );

    const error = await staffApi()
      .me.get(apiOptions(new AbortController().signal))
      .catch((value: unknown) => value);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).message).not.toContain("private body");
    expect((error as ApiError).traceId).toBeUndefined();
  });

  it("propagates cancellation without reporting a connection failure", async () => {
    const error = new DOMException("cancelled", "AbortError");
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(error));

    await expect(staffApi().me.get(apiOptions(new AbortController().signal))).rejects.toBe(error);
  });

  it("serializes nested WebAuthn data through the generated request model", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValue(new Response("{}", { headers: { "Content-Type": "application/json" } }));
    vi.stubGlobal("fetch", fetch);
    const assertion = {
      id: "test-key",
      type: "public-key",
      response: { authenticatorData: "test-data", signature: "test-signature", userHandle: null },
      clientExtensionResults: { appid: false },
    };

    await staffApi().staff.passkey.complete.post(
      { challenge: "test-challenge", response: { additionalData: assertion } },
      apiOptions(new AbortController().signal),
    );

    const init = fetch.mock.calls[0]?.[1] as RequestInit;
    expect(await new Response(init.body).json()).toEqual({
      challenge: "test-challenge",
      response: assertion,
    });
  });

  it("does not retry credential requests when the server rate limits them", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValue(new Response("{}", { status: 429, headers: { "Retry-After": "15" } }));
    vi.stubGlobal("fetch", fetch);

    const error = await staffApi()
      .staff.password.post(
        { login: "teacher", password: "test-only" },
        apiOptions(new AbortController().signal),
      )
      .catch((value: unknown) => value);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).retryAfterSeconds).toBe(15);
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it.each([429, 500, 502, 503])(
    "classifies HTTP %i as a transient server failure",
    async (status) => {
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("{}", { status })));

      const error = await staffApi()
        .staff.refresh.post(
          { refreshToken: "test-refresh" },
          apiOptions(new AbortController().signal),
        )
        .catch((value: unknown) => value);

      expect(error).toBeInstanceOf(ApiError);
      expect((error as ApiError).status).toBe(status);
      expect((error as ApiError).transient).toBe(true);
    },
  );

  it("keeps malformed responses distinct from an HTTP gateway failure", () => {
    expect(invalidResponse().status).toBe(502);
    expect(invalidResponse().transient).toBe(false);
    expect(toApiError(new SyntaxError("malformed response")).transient).toBe(false);
    expect(toApiError(new TypeError("network failure")).transient).toBe(true);
  });
});
