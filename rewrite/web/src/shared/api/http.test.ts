import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError, requestJson } from "./http";

afterEach(() => vi.unstubAllGlobals());

describe("API transport", () => {
  it("sends credentials in a noncached body with the browser cookie", async () => {
    const fetch = vi.fn().mockResolvedValue(new Response("{}", { status: 200 }));
    vi.stubGlobal("fetch", fetch);
    const signal = new AbortController().signal;

    await requestJson("/api/auth/staff/password", {
      method: "POST",
      body: { login: "teacher", password: "test-only" },
      signal,
    });

    expect(fetch).toHaveBeenCalledWith(
      "/api/auth/staff/password",
      expect.objectContaining({
        credentials: "include",
        cache: "no-store",
        redirect: "error",
        signal,
        body: JSON.stringify({ login: "teacher", password: "test-only" }),
      }),
    );
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

    const error = await requestJson("/api/auth/me", { signal: new AbortController().signal }).catch(
      (value: unknown) => value,
    );

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).message).not.toContain("private body");
    expect((error as ApiError).traceId).toBeUndefined();
  });

  it("propagates cancellation without reporting a connection failure", async () => {
    const error = new DOMException("cancelled", "AbortError");
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(error));

    await expect(
      requestJson("/api/auth/me", { signal: new AbortController().signal }),
    ).rejects.toBe(error);
  });
});
