import { act, cleanup, renderHook, waitFor } from "@testing-library/react";
import { type ReactNode, StrictMode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError, apiOptions } from "@/shared/api";
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

async function renderAnonymousSession() {
  const credentialFetch = globalThis.fetch;
  const restore = vi
    .fn()
    .mockImplementation(() => Promise.resolve(new Response("{}", { status: 401 })));
  vi.stubGlobal("fetch", restore);
  const hook = renderHook(useStaffSession, { wrapper });
  await act(() => hook.result.current.restoreSession());
  expect(restore).toHaveBeenCalledOnce();
  expect(restore.mock.calls[0]?.[0]).toContain("/api/auth/staff/restore");
  expect(hook.result.current.status).toBe("anonymous");
  expect(hook.result.current.error).toBeNull();
  vi.stubGlobal("fetch", credentialFetch);
  return hook;
}

function installCookieLock() {
  let tail = Promise.resolve();
  const request = vi.fn((_name: string, options: LockOptions, callback: () => Promise<unknown>) => {
    const task = tail.then(() => {
      options.signal?.throwIfAborted();
      return callback();
    });
    tail = task.then(
      () => undefined,
      () => undefined,
    );
    return task;
  });
  vi.stubGlobal("navigator", { locks: { request }, onLine: true });
  return request;
}

const discoveryOptions = JSON.stringify({
  challenge: "AQID",
  rpId: "localhost",
  userVerification: "required",
  allowCredentials: [],
});

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
    getClientExtensionResults() {
      return {};
    }
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
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("staff session", () => {
  it("passes a password only after the second factor completes", async () => {
    const onAuthenticated = vi.fn();
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(new Response("{}", { status: 401 }))
      .mockResolvedValueOnce(
        response({ challenge: "factor-proof", factor: "authenticator", optionsJson: null }),
      )
      .mockResolvedValueOnce(response(credentials))
      .mockResolvedValueOnce(response(identity));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const lifecycleWrapper = ({ children }: { children: ReactNode }) => (
      <StaffSessionProvider onAuthenticated={onAuthenticated}>{children}</StaffSessionProvider>
    );
    const { result } = renderHook(useStaffSession, { wrapper: lifecycleWrapper });
    await waitFor(() => expect(fetch).toHaveBeenCalledOnce());

    await act(() => result.current.signInWithPassword("staff.demo", "private-password"));

    expect(onAuthenticated).not.toHaveBeenCalled();
    expect(result.current.factor).toEqual({ method: "authenticator", canUsePasskey: false });

    await act(() => result.current.completeFactor("123456"));

    expect(onAuthenticated).toHaveBeenCalledOnce();
    expect(onAuthenticated).toHaveBeenCalledWith(identity, {
      source: "credential",
      password: "private-password",
    });
  });

  it("drops a pending password when sign-in is cancelled", async () => {
    installPasskeyChooser();
    const onAuthenticated = vi.fn();
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(new Response("{}", { status: 401 }))
      .mockResolvedValueOnce(
        response({ challenge: "factor-proof", factor: "authenticator", optionsJson: null }),
      )
      .mockResolvedValueOnce(
        response({ challenge: "passkey-proof", optionsJson: discoveryOptions }),
      )
      .mockResolvedValueOnce(response(credentials))
      .mockResolvedValueOnce(response(identity));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const lifecycleWrapper = ({ children }: { children: ReactNode }) => (
      <StaffSessionProvider onAuthenticated={onAuthenticated}>{children}</StaffSessionProvider>
    );
    const { result } = renderHook(useStaffSession, { wrapper: lifecycleWrapper });
    await waitFor(() => expect(fetch).toHaveBeenCalledOnce());

    await act(() => result.current.signInWithPassword("staff.demo", "private-password"));
    act(() => result.current.cancelSignIn());
    await act(() => result.current.preparePasskey());
    await act(() => result.current.signInWithPasskey());

    expect(onAuthenticated).toHaveBeenCalledWith(identity, { source: "credential" });
    expect(onAuthenticated.mock.calls[0]?.[1]).not.toHaveProperty("password");
  });

  it("identifies cookie restoration without passing a password", async () => {
    const onAuthenticated = vi.fn();
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(response(credentials))
      .mockResolvedValueOnce(response(identity));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const lifecycleWrapper = ({ children }: { children: ReactNode }) => (
      <StaffSessionProvider onAuthenticated={onAuthenticated}>{children}</StaffSessionProvider>
    );
    renderHook(useStaffSession, { wrapper: lifecycleWrapper });

    await waitFor(() => expect(onAuthenticated).toHaveBeenCalledOnce());

    expect(onAuthenticated).toHaveBeenCalledWith(identity, { source: "restore" });
  });

  it("updates effective permissions in the background without restarting the authenticated session", async () => {
    const updated = {
      ...identity,
      permissions: [
        { permission: "clientsRead", allowed: false, dataScope: "available", timeScope: "any" },
      ],
    };
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(response(credentials))
      .mockResolvedValueOnce(response(identity))
      .mockResolvedValueOnce(response(updated));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = await renderAnonymousSession();
    await act(() => result.current.signInWithPassword("staff.demo", "demo-password"));

    await act(() => result.current.synchronizeIdentity(new AbortController().signal));

    expect(result.current.status).toBe("authenticated");
    expect(result.current.staff).toEqual(updated);
    expect(fetch.mock.calls[2]?.[0]).toContain("/api/auth/me");
    expect(fetch.mock.calls).toHaveLength(3);
  });

  it("aborts resource requests when the account is cleared and rejects their late results", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(response(credentials))
        .mockResolvedValueOnce(response(identity)),
    );
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = await renderAnonymousSession();
    await act(() => result.current.signInWithPassword("staff.demo", "demo-password"));
    let finish: (() => void) | undefined;
    let resourceSignal: AbortSignal | undefined;
    const deferred = new Promise<void>((resolve) => {
      finish = resolve;
    });
    const pending = result.current.request(async (_api, signal) => {
      resourceSignal = signal;
      await deferred;
      return "old account data";
    }, new AbortController().signal);
    const rejected = expect(pending).rejects.toMatchObject({ name: "AbortError" });

    act(() => result.current.cancelSignIn());
    finish?.();

    await rejected;
    expect(resourceSignal?.aborted).toBe(true);
    expect(result.current.staff).toBeNull();
  });

  it("prepares a discoverable passkey without a username or opening the chooser", async () => {
    const get = installPasskeyChooser();
    const fetch = vi
      .fn()
      .mockResolvedValue(response({ challenge: "test-proof", optionsJson: discoveryOptions }));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = await renderAnonymousSession();

    await act(() => result.current.preparePasskey());

    expect(result.current.passkeyReady).toBe(true);
    expect(result.current.preparingPasskey).toBe(false);
    expect(fetch.mock.calls[0]?.[0]).toContain("/api/auth/staff/passkey/options");
    expect((fetch.mock.calls[0]?.[1] as RequestInit | undefined)?.body).toBeUndefined();
    expect(get).not.toHaveBeenCalled();
  });

  it("opens the system chooser directly from the prepared sign-in action", async () => {
    const get = installPasskeyChooser();
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(response({ challenge: "test-proof", optionsJson: discoveryOptions }))
      .mockResolvedValueOnce(response(credentials))
      .mockResolvedValueOnce(response(identity));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = await renderAnonymousSession();
    await act(() => result.current.preparePasskey());
    let pending: Promise<void> | undefined;

    act(() => {
      pending = result.current.signInWithPasskey();
      expect(get).toHaveBeenCalledOnce();
      expect(fetch).toHaveBeenCalledTimes(1);
    });
    await act(async () => {
      await pending;
    });

    const options = get.mock.calls[0]?.[0] as CredentialRequestOptions;
    expect(options.publicKey?.allowCredentials).toEqual([]);
    expect(result.current.staff).toEqual(identity);
    expect(fetch.mock.calls[1]?.[0]).toContain("/api/auth/staff/passkey/complete");
  });

  it("keeps the account closed when the selected passkey requires TOTP", async () => {
    installPasskeyChooser();
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(response({ challenge: "test-proof", optionsJson: discoveryOptions }))
      .mockResolvedValueOnce(
        response({ challenge: "test-factor", factor: "authenticator", optionsJson: null }),
      );
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = await renderAnonymousSession();
    await act(() => result.current.preparePasskey());

    await act(() => result.current.signInWithPasskey());

    expect(result.current.staff).toBeNull();
    expect(result.current.factor).toEqual({ method: "authenticator", canUsePasskey: false });
    expect(fetch).toHaveBeenCalledTimes(2);
  });

  it("does not restore a prepared proof after the user cancels sign-in", async () => {
    let resolve: ((value: Response) => void) | undefined;
    vi.stubGlobal(
      "fetch",
      vi.fn().mockReturnValue(
        new Promise<Response>((complete) => {
          resolve = complete;
        }),
      ),
    );
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = await renderAnonymousSession();
    let pending: Promise<void> | undefined;
    act(() => {
      pending = result.current.preparePasskey();
    });

    act(() => result.current.cancelSignIn());
    await act(async () => {
      resolve?.(response({ challenge: "late-proof", optionsJson: discoveryOptions }));
      await pending;
    });

    expect(result.current.passkeyReady).toBe(false);
    expect(result.current.preparingPasskey).toBe(false);
  });

  it("opens the registered account only after reading its server identity", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(response(credentials))
      .mockResolvedValueOnce(response(identity));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = await renderAnonymousSession();
    let accepted: boolean | undefined;

    await act(async () => {
      accepted = await result.current.completeRegistration("test-challenge", "123456");
    });

    expect(accepted).toBe(true);
    expect(result.current.staff).toEqual(identity);
    const request = fetch.mock.calls[0]?.[1] as RequestInit;
    expect(await new Response(request.body).json()).toEqual({
      challenge: "test-challenge",
      authenticatorCode: "123456",
    });
    expect(fetch.mock.calls[1]?.[0]).toContain("/api/auth/me");
  });

  it("keeps registration awaiting its required passkey assertion", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValue(
        response({ challenge: "test-factor", factor: "authenticatorOrPasskey", optionsJson: "{}" }),
      );
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = await renderAnonymousSession();
    let accepted: boolean | undefined;

    await act(async () => {
      accepted = await result.current.completeRegistration("test-challenge", undefined, {
        id: "test-key",
      });
    });

    expect(accepted).toBe(true);
    expect(result.current.staff).toBeNull();
    expect(result.current.factor).toEqual({
      method: "authenticatorOrPasskey",
      canUsePasskey: true,
    });
    expect(fetch).toHaveBeenCalledTimes(1);
    const request = fetch.mock.calls[0]?.[1] as RequestInit;
    expect(await new Response(request.body).json()).toEqual({
      challenge: "test-challenge",
      passkeyResponse: { id: "test-key" },
    });
  });

  it("does not retry a consumed registration proof after a failed confirmation", async () => {
    const fetch = vi.fn().mockResolvedValue(new Response("{}", { status: 401 }));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = await renderAnonymousSession();
    let accepted: boolean | undefined;

    await act(async () => {
      accepted = await result.current.completeRegistration("test-challenge", "000000");
    });

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
    const { result } = await renderAnonymousSession();

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
    const { result } = await renderAnonymousSession();

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
    const { result } = await renderAnonymousSession();
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
    const { result } = await renderAnonymousSession();
    await act(() => result.current.signInWithPassword("teacher", "test-only"));

    await act(() => result.current.signOut());

    expect(result.current.staff).toEqual(identity);
    expect(result.current.status).toBe("authenticated");
    expect(result.current.error?.message).toContain("Не удалось выйти");
  });

  it("keeps the session active and retries logout when local data cannot be locked", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(new Response("{}", { status: 401 }))
      .mockResolvedValueOnce(response(credentials))
      .mockResolvedValueOnce(response(identity))
      .mockResolvedValueOnce(response({}));
    const onLogout = vi
      .fn()
      .mockRejectedValueOnce(new Error("sensitive local storage detail"))
      .mockResolvedValueOnce(undefined);
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const lifecycleWrapper = ({ children }: { children: ReactNode }) => (
      <StaffSessionProvider onLogout={onLogout}>{children}</StaffSessionProvider>
    );
    const { result } = renderHook(useStaffSession, { wrapper: lifecycleWrapper });
    await waitFor(() => expect(fetch).toHaveBeenCalledOnce());
    await act(() => result.current.signInWithPassword("staff.demo", "demo-password"));

    await act(async () => {
      await expect(result.current.signOut()).rejects.toMatchObject({
        name: "ApiError",
        message: "Не удалось закрыть локальные данные. Повторите выход.",
      });
    });

    expect(fetch).toHaveBeenCalledTimes(3);
    expect(onLogout).toHaveBeenCalledOnce();
    expect(result.current.staff).toEqual(identity);
    expect(result.current.status).toBe("authenticated");
    expect(result.current.error).toBeInstanceOf(ApiError);
    expect(result.current.error?.message).not.toContain("sensitive local storage detail");

    await act(() => result.current.signOut());

    expect(onLogout).toHaveBeenCalledTimes(2);
    expect(fetch).toHaveBeenCalledTimes(4);
    expect(new URL(String(fetch.mock.calls[3]?.[0])).pathname).toBe("/api/auth/logout");
    expect(result.current.staff).toBeNull();
    expect(result.current.status).toBe("anonymous");
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
    const { result } = await renderAnonymousSession();
    await act(() => result.current.signInWithPassword("teacher", "test-only"));

    await act(() => vi.advanceTimersByTimeAsync(270_000));

    expect(result.current.staff).toEqual(updatedIdentity);
    expect(fetch).toHaveBeenNthCalledWith(
      3,
      expect.stringContaining("/api/auth/staff/restore"),
      expect.any(Object),
    );
    const refreshRequest = fetch.mock.calls[2]?.[1] as RequestInit;
    expect(refreshRequest.body).toBeUndefined();
    expect(refreshRequest.credentials).toBe("include");
  });

  it.each([429, 503])(
    "keeps the account after refresh HTTP %i and retries only on a connectivity event",
    async (status) => {
      vi.useFakeTimers();
      vi.spyOn(document, "visibilityState", "get").mockReturnValue("visible");
      vi.spyOn(navigator, "onLine", "get").mockReturnValue(true);
      const fetch = vi
        .fn()
        .mockResolvedValueOnce(response(credentials))
        .mockResolvedValueOnce(response(identity))
        .mockResolvedValueOnce(new Response("{}", { status }))
        .mockResolvedValueOnce(
          response({
            ...credentials,
            accessToken: "rotated-access",
            refreshToken: "rotated-refresh",
          }),
        )
        .mockResolvedValueOnce(response(identity));
      vi.stubGlobal("fetch", fetch);
      vi.stubGlobal("BroadcastChannel", undefined);
      const { result } = await renderAnonymousSession();
      await act(() => result.current.signInWithPassword("staff.demo", "demo-password"));

      await act(() => vi.advanceTimersByTimeAsync(270_000));

      expect(result.current.staff).toEqual(identity);
      expect(result.current.status).toBe("authenticated");
      expect(result.current.error?.status).toBe(status);
      await act(() => vi.advanceTimersByTimeAsync(540_000));
      expect(fetch).toHaveBeenCalledTimes(3);

      await act(async () => {
        window.dispatchEvent(new Event("online"));
        await result.current.request(async () => undefined, new AbortController().signal);
      });

      expect(fetch).toHaveBeenCalledTimes(5);
      expect(result.current.staff).toEqual(identity);
      expect(result.current.error).toBeNull();
    },
  );

  it("does not sign out when refresh loses the network connection", async () => {
    vi.useFakeTimers();
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(response(credentials))
        .mockResolvedValueOnce(response(identity))
        .mockRejectedValueOnce(new TypeError("network unavailable")),
    );
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = await renderAnonymousSession();
    await act(() => result.current.signInWithPassword("staff.demo", "demo-password"));

    await act(() => vi.advanceTimersByTimeAsync(270_000));

    expect(result.current.staff).toEqual(identity);
    expect(result.current.error?.transient).toBe(true);
  });

  it("ends a session when refresh is explicitly rejected", async () => {
    vi.useFakeTimers();
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(response(credentials))
        .mockResolvedValueOnce(response(identity))
        .mockResolvedValueOnce(new Response("{}", { status: 401 })),
    );
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = await renderAnonymousSession();
    await act(() => result.current.signInWithPassword("staff.demo", "demo-password"));

    await act(() => vi.advanceTimersByTimeAsync(270_000));

    expect(result.current.staff).toBeNull();
    expect(result.current.status).toBe("anonymous");
    expect(result.current.error?.status).toBe(401);
  });

  it("fails closed when refreshed identity belongs to another account", async () => {
    vi.useFakeTimers();
    vi.stubGlobal(
      "fetch",
      vi
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
        .mockResolvedValueOnce(
          response({ ...identity, staffId: "019a2c63-8c00-7000-8000-000000000002" }),
        ),
    );
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = await renderAnonymousSession();
    await act(() => result.current.signInWithPassword("staff.demo", "demo-password"));

    await act(() => vi.advanceTimersByTimeAsync(270_000));

    expect(result.current.staff).toBeNull();
    expect(result.current.error?.status).toBe(502);
    expect(result.current.error?.transient).toBe(false);
  });

  it("waits for a concurrent refresh before retrying an expired resource once", async () => {
    vi.useFakeTimers();
    let finishRead: ((value: Response) => void) | undefined;
    let finishRefresh: ((value: Response) => void) | undefined;
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(response(credentials))
      .mockResolvedValueOnce(response(identity))
      .mockImplementationOnce(
        () =>
          new Promise<Response>((resolve) => {
            finishRead = resolve;
          }),
      )
      .mockImplementationOnce(
        () =>
          new Promise<Response>((resolve) => {
            finishRefresh = resolve;
          }),
      )
      .mockResolvedValueOnce(response(identity))
      .mockResolvedValueOnce(response(identity));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = await renderAnonymousSession();
    await act(() => result.current.signInWithPassword("staff.demo", "demo-password"));
    const read = result.current.request(
      (api, signal) => api.auth.me.get(apiOptions(signal)),
      new AbortController().signal,
    );
    await act(() => vi.advanceTimersByTimeAsync(270_000));

    await act(async () => {
      finishRead?.(new Response("{}", { status: 401 }));
    });
    expect(fetch).toHaveBeenCalledTimes(4);
    await act(async () => {
      finishRefresh?.(
        response({
          ...credentials,
          accessToken: "rotated-access",
          refreshToken: "rotated-refresh",
        }),
      );
      await read;
    });

    expect(result.current.staff).toEqual(identity);
    expect(fetch).toHaveBeenCalledTimes(6);
    expect(
      fetch.mock.calls.filter(([url]) => String(url).includes("/api/auth/staff/restore")),
    ).toHaveLength(1);
    const retry = fetch.mock.calls[5]?.[1] as RequestInit;
    expect(new Headers(retry.headers).get("Authorization")).toBe("Bearer rotated-access");
  });

  it("does not replay an expired read after a transient refresh failure", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(response(credentials))
        .mockResolvedValueOnce(response(identity))
        .mockResolvedValueOnce(new Response("{}", { status: 401 }))
        .mockResolvedValueOnce(new Response("{}", { status: 503 })),
    );
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = await renderAnonymousSession();
    await act(() => result.current.signInWithPassword("staff.demo", "demo-password"));
    let failure: unknown;

    await act(async () => {
      failure = await result.current
        .request((api, signal) => api.auth.me.get(apiOptions(signal)), new AbortController().signal)
        .catch((error: unknown) => error);
    });

    expect(failure).toBeInstanceOf(ApiError);
    expect((failure as ApiError).status).toBe(503);
    expect(result.current.staff).toEqual(identity);
  });

  it("does not refresh again when the resource rejects the rotated token", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(response(credentials))
      .mockResolvedValueOnce(response(identity))
      .mockResolvedValueOnce(new Response("{}", { status: 401 }))
      .mockResolvedValueOnce(
        response({
          ...credentials,
          accessToken: "rotated-access",
          refreshToken: "rotated-refresh",
        }),
      )
      .mockResolvedValueOnce(response(identity))
      .mockResolvedValueOnce(new Response("{}", { status: 401 }));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = await renderAnonymousSession();
    await act(() => result.current.signInWithPassword("staff.demo", "demo-password"));
    let failure: unknown;

    await act(async () => {
      failure = await result.current
        .request((api, signal) => api.auth.me.get(apiOptions(signal)), new AbortController().signal)
        .catch((error: unknown) => error);
    });

    expect(failure).toBeInstanceOf(ApiError);
    expect((failure as ApiError).status).toBe(401);
    expect(result.current.staff).toBeNull();
    expect(
      fetch.mock.calls.filter(([url]) => String(url).includes("/api/auth/staff/restore")),
    ).toHaveLength(1);
  });

  it("aborts a read waiting for refresh when the account is cleared", async () => {
    vi.useFakeTimers();
    let finishRefresh: ((value: Response) => void) | undefined;
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(response(credentials))
        .mockResolvedValueOnce(response(identity))
        .mockImplementationOnce(
          () =>
            new Promise<Response>((resolve) => {
              finishRefresh = resolve;
            }),
        ),
    );
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = await renderAnonymousSession();
    await act(() => result.current.signInWithPassword("staff.demo", "demo-password"));
    await act(() => vi.advanceTimersByTimeAsync(270_000));
    const send = vi.fn().mockResolvedValue("account data");
    const pending = result.current.request(send, new AbortController().signal);
    const rejected = expect(pending).rejects.toMatchObject({ name: "AbortError" });

    act(() => result.current.cancelSignIn());
    await rejected;
    await act(async () => {
      finishRefresh?.(response({ ...credentials, accessToken: "late-token" }));
    });

    expect(send).not.toHaveBeenCalled();
    expect(result.current.staff).toBeNull();
  });
});

describe("browser session restoration", () => {
  it("restores a fresh provider through the browser cookie without storing credentials in JavaScript storage", async () => {
    const storageRead = vi.spyOn(Storage.prototype, "getItem");
    const storageWrite = vi.spyOn(Storage.prototype, "setItem");
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(response(credentials))
      .mockResolvedValueOnce(response(identity))
      .mockResolvedValueOnce(
        response({
          ...credentials,
          accessToken: "restored-access",
          refreshToken: "restored-refresh",
        }),
      )
      .mockResolvedValueOnce(response(identity));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const first = renderHook(useStaffSession, { wrapper });
    expect(first.result.current.status).toBe("restoring");
    await act(() => first.result.current.restoreSession());
    expect(first.result.current.staff).toEqual(identity);

    first.unmount();
    const second = renderHook(useStaffSession, { wrapper });
    expect(second.result.current.status).toBe("restoring");
    expect(second.result.current.staff).toBeNull();
    await act(() => second.result.current.restoreSession());

    expect(second.result.current.staff).toEqual(identity);
    const restores = fetch.mock.calls.filter(([url]) =>
      String(url).includes("/api/auth/staff/restore"),
    );
    expect(restores).toHaveLength(2);
    for (const [, request] of restores) {
      const init = request as RequestInit;
      expect(init.credentials).toBe("include");
      expect(init.cache).toBe("no-store");
      expect(init.body).toBeUndefined();
      expect(new Headers(init.headers).has("Authorization")).toBe(false);
    }
    expect(storageRead).not.toHaveBeenCalled();
    expect(storageWrite).not.toHaveBeenCalled();
  });

  it("shows ordinary signed-out state when the restoration cookie is absent or expired", async () => {
    const fetch = vi.fn().mockResolvedValue(new Response("{}", { status: 401 }));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = renderHook(useStaffSession, { wrapper });
    expect(result.current.status).toBe("restoring");

    await act(() => result.current.restoreSession());

    expect(result.current.status).toBe("anonymous");
    expect(result.current.staff).toBeNull();
    expect(result.current.error).toBeNull();
    expect(fetch).toHaveBeenCalledOnce();
  });

  it.each([429, 503])(
    "keeps restoration recoverable after HTTP %i and retries explicitly",
    async (status) => {
      const fetch = vi
        .fn()
        .mockResolvedValueOnce(new Response("{}", { status }))
        .mockResolvedValueOnce(response(credentials))
        .mockResolvedValueOnce(response(identity));
      vi.stubGlobal("fetch", fetch);
      vi.stubGlobal("BroadcastChannel", undefined);
      const { result } = renderHook(useStaffSession, { wrapper });

      await act(() => result.current.restoreSession());

      expect(result.current.status).toBe("restoring");
      expect(result.current.staff).toBeNull();
      expect(result.current.error?.status).toBe(status);
      expect(fetch).toHaveBeenCalledOnce();

      await act(() => result.current.restoreSession());

      expect(result.current.status).toBe("authenticated");
      expect(result.current.staff).toEqual(identity);
      expect(result.current.error).toBeNull();
      expect(fetch).toHaveBeenCalledTimes(3);
    },
  );

  it("keeps the restoration screen on a connection failure instead of flashing sign-in", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network unavailable")));
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = renderHook(useStaffSession, { wrapper });

    await act(() => result.current.restoreSession());

    expect(result.current.status).toBe("restoring");
    expect(result.current.staff).toBeNull();
    expect(result.current.error?.transient).toBe(true);
  });

  it("shares one cookie restoration across StrictMode effect remounts without aborting rotation", async () => {
    let finish: ((value: Response) => void) | undefined;
    const fetch = vi
      .fn()
      .mockImplementationOnce(
        () =>
          new Promise<Response>((resolve) => {
            finish = resolve;
          }),
      )
      .mockResolvedValueOnce(response(identity));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const strictWrapper = ({ children }: { children: ReactNode }) => (
      <StrictMode>
        <StaffSessionProvider>{children}</StaffSessionProvider>
      </StrictMode>
    );
    const { result } = renderHook(useStaffSession, { wrapper: strictWrapper });
    await waitFor(() => expect(fetch).toHaveBeenCalledOnce());
    const restoreRequest = fetch.mock.calls[0]?.[1] as RequestInit;
    expect(restoreRequest.signal?.aborted).toBe(false);

    await act(async () => {
      finish?.(response(credentials));
      await result.current.restoreSession();
    });

    expect(result.current.staff).toEqual(identity);
    expect(result.current.status).toBe("authenticated");
    expect(fetch).toHaveBeenCalledTimes(2);
    expect(
      fetch.mock.calls.filter(([url]) => String(url).includes("/api/auth/staff/restore")),
    ).toHaveLength(1);
  });

  it("does not broadcast an account switch when the active account is only being restored", async () => {
    const postMessage = vi.fn();
    class Channel {
      onmessage: ((event: MessageEvent<unknown>) => void) | null = null;
      postMessage = postMessage;
      close() {}
    }
    vi.stubGlobal("BroadcastChannel", Channel);
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(response(credentials))
        .mockResolvedValueOnce(response(identity)),
    );
    const { result } = renderHook(useStaffSession, { wrapper });

    await act(() => result.current.restoreSession());

    expect(result.current.staff).toEqual(identity);
    expect(postMessage).not.toHaveBeenCalled();
  });

  it("clears the active account on an incoming change and ignores unrelated messages", async () => {
    const channels: Array<{ onmessage: ((event: MessageEvent<unknown>) => void) | null }> = [];
    class Channel {
      onmessage: ((event: MessageEvent<unknown>) => void) | null = null;
      postMessage = vi.fn();
      close = vi.fn();

      constructor() {
        channels.push(this);
      }
    }
    vi.stubGlobal("BroadcastChannel", Channel);
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(response(credentials))
        .mockResolvedValueOnce(response(identity)),
    );
    const { result } = await renderAnonymousSession();
    await act(() => result.current.signInWithPassword("staff.demo", "demo-password"));
    let finish: (() => void) | undefined;
    let resourceSignal: AbortSignal | undefined;
    const deferred = new Promise<void>((resolve) => {
      finish = resolve;
    });
    const pending = result.current.request(async (_api, signal) => {
      resourceSignal = signal;
      await deferred;
      return "late account data";
    }, new AbortController().signal);
    const rejected = expect(pending).rejects.toMatchObject({ name: "AbortError" });

    act(() => channels[0]?.onmessage?.({ data: "unrelated" } as MessageEvent<unknown>));
    expect(result.current.staff).toEqual(identity);
    expect(resourceSignal?.aborted).toBe(false);

    act(() => channels[0]?.onmessage?.({ data: "changed" } as MessageEvent<unknown>));
    finish?.();

    await rejected;
    expect(resourceSignal?.aborted).toBe(true);
    expect(result.current.staff).toBeNull();
    expect(result.current.status).toBe("anonymous");
    expect(result.current.error?.status).toBe(401);
  });

  it("preserves an active session after a transient logout failure", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(response(credentials))
      .mockResolvedValueOnce(response(identity))
      .mockResolvedValueOnce(new Response("{}", { status: 503 }));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = await renderAnonymousSession();
    await act(() => result.current.signInWithPassword("staff.demo", "demo-password"));

    await act(() => result.current.signOut());

    expect(result.current.staff).toEqual(identity);
    expect(result.current.status).toBe("authenticated");
    expect(result.current.error?.status).toBe(503);
    expect(fetch.mock.calls.map(([url]) => new URL(String(url)).pathname)).toEqual([
      "/api/auth/staff/password",
      "/api/auth/me",
      "/api/auth/logout",
    ]);
  });

  it("finishes an old cookie restoration before writing another sign-in and ignores its account", async () => {
    let finish: ((value: Response) => void) | undefined;
    const lock = installCookieLock();
    const nextIdentity = {
      ...identity,
      staffId: "019a2c63-8c00-7000-8000-000000000002",
      login: "another.staff",
    };
    const fetch = vi
      .fn()
      .mockImplementationOnce(
        () =>
          new Promise<Response>((resolve) => {
            finish = resolve;
          }),
      )
      .mockResolvedValueOnce(response(identity))
      .mockResolvedValueOnce(
        response({ ...credentials, staffId: nextIdentity.staffId, accessToken: "next-access" }),
      )
      .mockResolvedValueOnce(response(nextIdentity));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = renderHook(useStaffSession, { wrapper });
    await waitFor(() => expect(fetch).toHaveBeenCalledOnce());
    let signingIn: Promise<void> | undefined;

    act(() => {
      signingIn = result.current.signInWithPassword("another.staff", "demo-password");
    });
    expect(fetch).toHaveBeenCalledOnce();
    await act(async () => {
      finish?.(response(credentials));
      await signingIn;
    });

    expect(result.current.staff).toEqual(nextIdentity);
    expect(result.current.status).toBe("authenticated");
    expect(fetch.mock.calls.map(([url]) => new URL(String(url)).pathname)).toEqual([
      "/api/auth/staff/restore",
      "/api/auth/me",
      "/api/auth/staff/password",
      "/api/auth/me",
    ]);
    expect(lock).toHaveBeenCalledTimes(2);
    expect(lock.mock.calls.every(([name]) => name === "melodytrack.staff.restore")).toBe(true);
  });

  it("orders logout after an in-flight cookie refresh and does not restore again after logout", async () => {
    vi.useFakeTimers();
    installCookieLock();
    vi.spyOn(document, "visibilityState", "get").mockReturnValue("visible");
    let finishRefresh: ((value: Response) => void) | undefined;
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(response(credentials))
      .mockResolvedValueOnce(response(identity))
      .mockImplementationOnce(
        () =>
          new Promise<Response>((resolve) => {
            finishRefresh = resolve;
          }),
      )
      .mockResolvedValueOnce(response(identity))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("BroadcastChannel", undefined);
    const { result } = renderHook(useStaffSession, { wrapper });
    await act(() => result.current.restoreSession());
    await act(() => vi.advanceTimersByTimeAsync(270_000));
    let logout: Promise<void> | undefined;

    act(() => {
      logout = result.current.signOut();
    });
    expect(fetch).toHaveBeenCalledTimes(3);
    await act(async () => {
      finishRefresh?.(
        response({ ...credentials, accessToken: "late-access", refreshToken: "late-refresh" }),
      );
      await logout;
    });

    expect(result.current.staff).toBeNull();
    expect(result.current.status).toBe("anonymous");
    expect(fetch.mock.calls.map(([url]) => new URL(String(url)).pathname)).toEqual([
      "/api/auth/staff/restore",
      "/api/auth/me",
      "/api/auth/staff/restore",
      "/api/auth/me",
      "/api/auth/logout",
    ]);
    await act(async () => {
      window.dispatchEvent(new Event("online"));
      await vi.advanceTimersByTimeAsync(540_000);
    });
    expect(fetch).toHaveBeenCalledTimes(5);
  });
});
