import { act, cleanup, renderHook } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StaffSessionProvider, useStaffSession } from "./StaffSessionProvider";

const identity = { staffId: "staff-a", role: "teacher", permissions: [] };
const credentials = {
  accessToken: "test-access",
  refreshToken: "test-refresh",
  staffId: identity.staffId,
  expiresIn: 300,
};
const response = (body: unknown) => new Response(JSON.stringify(body), { status: 200 });
const wrapper = ({ children }: { children: ReactNode }) => (
  <StaffSessionProvider>{children}</StaffSessionProvider>
);

afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("staff session", () => {
  it("reads current server identity before opening staff screens", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(response(credentials))
      .mockResolvedValueOnce(response(identity));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = renderHook(useStaffSession, { wrapper });

    await act(() => result.current.signInWithPassword("teacher", "test-only"));

    expect(result.current.staff).toEqual(identity);
    expect(result.current.status).toBe("authenticated");
    expect(fetch).toHaveBeenNthCalledWith(
      2,
      "/api/auth/me",
      expect.objectContaining({
        headers: expect.any(Headers),
        credentials: "include",
        cache: "no-store",
      }),
    );
    const identityRequest = fetch.mock.calls[1]?.[1] as RequestInit;
    expect(new Headers(identityRequest.headers).get("Authorization")).toBe("Bearer test-access");
  });

  it("does not authenticate a challenge requiring another factor", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValue(
        response({ challenge: "pending", factor: "authenticator", optionsJson: null }),
      );
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = renderHook(useStaffSession, { wrapper });

    await act(() => result.current.signInWithPassword("teacher", "test-only"));

    expect(result.current.staff).toBeNull();
    expect(result.current.factor).toEqual({ method: "authenticator", canUsePasskey: false });
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it("ignores a late successful response after the user cancels sign in", async () => {
    let resolve: ((value: Response) => void) | undefined;
    const pending = new Promise<Response>((complete) => {
      resolve = complete;
    });
    const fetch = vi.fn().mockReturnValue(pending);
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = renderHook(useStaffSession, { wrapper });
    let signIn: Promise<void> | undefined;

    act(() => {
      signIn = result.current.signInWithPassword("teacher", "test-only");
    });
    act(() => result.current.cancelSignIn());
    await act(async () => {
      resolve?.(response(credentials));
      await signIn;
    });

    expect(result.current.staff).toBeNull();
    expect(result.current.status).toBe("anonymous");
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it("keeps the account available for retry if server logout is not confirmed", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(response(credentials))
        .mockResolvedValueOnce(response(identity))
        .mockRejectedValueOnce(new TypeError("network unavailable")),
    );
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = renderHook(useStaffSession, { wrapper });
    await act(() => result.current.signInWithPassword("teacher", "test-only"));

    await act(() => result.current.signOut());

    expect(result.current.staff).toEqual(identity);
    expect(result.current.status).toBe("authenticated");
    expect(result.current.error?.message).toContain("Выход на сервере не подтверждён");
  });

  it("refreshes credentials and effective permissions together", async () => {
    vi.useFakeTimers();
    const updatedIdentity = {
      ...identity,
      permissions: [
        { permission: "scheduleRead", allowed: false, dataScope: "own", timeScope: "all" },
      ],
    };
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(response(credentials))
      .mockResolvedValueOnce(response(identity))
      .mockResolvedValueOnce(
        response({
          ...credentials,
          accessToken: "rotated-access",
          refreshToken: "rotated-refresh",
        }),
      )
      .mockResolvedValueOnce(response(updatedIdentity));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = renderHook(useStaffSession, { wrapper });
    await act(() => result.current.signInWithPassword("teacher", "test-only"));

    await act(() => vi.advanceTimersByTimeAsync(270_000));

    expect(result.current.staff).toEqual(updatedIdentity);
    expect(fetch).toHaveBeenNthCalledWith(
      3,
      "/api/auth/staff/refresh",
      expect.objectContaining({ body: JSON.stringify({ refreshToken: "test-refresh" }) }),
    );
  });
});
