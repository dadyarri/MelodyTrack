import { act, cleanup, renderHook } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StaffSessionProvider, useStaffSession } from "./StaffSessionProvider";

const identity = {
  staffId: "019a2c63-8c00-7000-8000-000000000001",
  login: "staff.demo",
  role: "teacher",
  permissions: [],
};
const credentials = {
  accessToken: "test-access",
  refreshToken: "test-refresh",
  staffId: identity.staffId,
  expiresIn: 300,
};
const response = (body: unknown) =>
  new Response(JSON.stringify(body), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
const wrapper = ({ children }: { children: ReactNode }) => (
  <StaffSessionProvider>{children}</StaffSessionProvider>
);

const discoveryOptions = JSON.stringify({ challenge: "AQID", rpId: "localhost", userVerification: "required", allowCredentials: [] });

function installPasskeyChooser() {
  class Assertion {
    authenticatorData = new Uint8Array([1, 2, 3]).buffer;
    clientDataJSON = new Uint8Array([4, 5, 6]).buffer;
    signature = new Uint8Array([7, 8, 9]).buffer;
    userHandle = new Uint8Array(16).buffer;
  }
  class Credential {
    id = "AQID";
    rawId = new Uint8Array([1, 2, 3]).buffer;
    type = "public-key";
    response = new Assertion();
    getClientExtensionResults() { return {}; }
  }
  const get = vi.fn().mockResolvedValue(new Credential());
  vi.stubGlobal("isSecureContext", true);
  vi.stubGlobal("PublicKeyCredential", Credential);
  vi.stubGlobal("AuthenticatorAssertionResponse", Assertion);
  vi.stubGlobal("navigator", { credentials: { get } });
  return get;
}

afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("staff session", () => {
  it("prepares a discoverable passkey without a username or opening the chooser", async () => {
    const get = installPasskeyChooser();
    const fetch = vi.fn().mockResolvedValue(response({ challenge: "test-proof", optionsJson: discoveryOptions }));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = renderHook(useStaffSession, { wrapper });

    await act(() => result.current.preparePasskey());

    expect(result.current.passkeyReady).toBe(true);
    expect(result.current.preparingPasskey).toBe(false);
    expect(fetch.mock.calls[0]?.[0]).toContain("/api/auth/staff/passkey/options");
    expect((fetch.mock.calls[0]?.[1] as RequestInit).body).toBeUndefined();
    expect(get).not.toHaveBeenCalled();
  });

  it("opens the system chooser directly from the prepared sign-in action", async () => {
    const get = installPasskeyChooser();
    const fetch = vi.fn().mockResolvedValueOnce(response({ challenge: "test-proof", optionsJson: discoveryOptions }))
      .mockResolvedValueOnce(response(credentials)).mockResolvedValueOnce(response(identity));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = renderHook(useStaffSession, { wrapper });
    await act(() => result.current.preparePasskey());
    let pending: Promise<void> | undefined;

    act(() => {
      pending = result.current.signInWithPasskey();
      expect(get).toHaveBeenCalledOnce();
      expect(fetch).toHaveBeenCalledTimes(1);
    });
    await act(async () => { await pending; });

    const options = get.mock.calls[0]?.[0] as CredentialRequestOptions;
    expect(options.publicKey?.allowCredentials).toEqual([]);
    expect(result.current.staff).toEqual(identity);
    expect(fetch.mock.calls[1]?.[0]).toContain("/api/auth/staff/passkey/complete");
  });

  it("keeps the account closed when the selected passkey requires TOTP", async () => {
    installPasskeyChooser();
    const fetch = vi.fn().mockResolvedValueOnce(response({ challenge: "test-proof", optionsJson: discoveryOptions }))
      .mockResolvedValueOnce(response({ challenge: "test-factor", factor: "authenticator", optionsJson: null }));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = renderHook(useStaffSession, { wrapper });
    await act(() => result.current.preparePasskey());

    await act(() => result.current.signInWithPasskey());

    expect(result.current.staff).toBeNull();
    expect(result.current.factor).toEqual({ method: "authenticator", canUsePasskey: false });
    expect(fetch).toHaveBeenCalledTimes(2);
  });

  it("does not restore a prepared proof after the user cancels sign-in", async () => {
    let resolve: ((value: Response) => void) | undefined;
    vi.stubGlobal("fetch", vi.fn().mockReturnValue(new Promise<Response>((complete) => { resolve = complete; })));
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = renderHook(useStaffSession, { wrapper });
    let pending: Promise<void> | undefined;
    act(() => { pending = result.current.preparePasskey(); });

    act(() => result.current.cancelSignIn());
    await act(async () => { resolve?.(response({ challenge: "late-proof", optionsJson: discoveryOptions })); await pending; });

    expect(result.current.passkeyReady).toBe(false);
    expect(result.current.preparingPasskey).toBe(false);
  });

  it("opens the registered account only after reading its server identity", async () => {
    const fetch = vi.fn().mockResolvedValueOnce(response(credentials)).mockResolvedValueOnce(response(identity));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = renderHook(useStaffSession, { wrapper });
    let accepted: boolean | undefined;

    await act(async () => { accepted = await result.current.completeRegistration("test-challenge", "123456"); });

    expect(accepted).toBe(true);
    expect(result.current.staff).toEqual(identity);
    const request = fetch.mock.calls[0]?.[1] as RequestInit;
    expect(await new Response(request.body).json()).toEqual({ challenge: "test-challenge", authenticatorCode: "123456" });
    expect(fetch.mock.calls[1]?.[0]).toContain("/api/auth/me");
  });

  it("keeps registration awaiting its required passkey assertion", async () => {
    const fetch = vi.fn().mockResolvedValue(response({ challenge: "test-factor", factor: "authenticatorOrPasskey", optionsJson: "{}" }));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = renderHook(useStaffSession, { wrapper });
    let accepted: boolean | undefined;

    await act(async () => { accepted = await result.current.completeRegistration("test-challenge", undefined, { id: "test-key" }); });

    expect(accepted).toBe(true);
    expect(result.current.staff).toBeNull();
    expect(result.current.factor).toEqual({ method: "authenticatorOrPasskey", canUsePasskey: true });
    expect(fetch).toHaveBeenCalledTimes(1);
    const request = fetch.mock.calls[0]?.[1] as RequestInit;
    expect(await new Response(request.body).json()).toEqual({ challenge: "test-challenge", passkeyResponse: { id: "test-key" } });
  });

  it("does not retry a consumed registration proof after a failed confirmation", async () => {
    const fetch = vi.fn().mockResolvedValue(new Response("{}", { status: 401 }));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = renderHook(useStaffSession, { wrapper });
    let accepted: boolean | undefined;

    await act(async () => { accepted = await result.current.completeRegistration("test-challenge", "000000"); });

    expect(accepted).toBe(false);
    expect(result.current.staff).toBeNull();
    expect(result.current.error?.status).toBe(401);
    expect(fetch).toHaveBeenCalledTimes(1);
  });

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
      expect.stringContaining("/api/auth/me"),
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
    expect(result.current.error?.message).toContain("Не удалось выйти");
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
      expect.stringContaining("/api/auth/staff/refresh"),
      expect.any(Object),
    );
    const refreshRequest = fetch.mock.calls[2]?.[1] as RequestInit;
    expect(await new Response(refreshRequest.body).json()).toEqual({
      refreshToken: "test-refresh",
    });
  });
});
