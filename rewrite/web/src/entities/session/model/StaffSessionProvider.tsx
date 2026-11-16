import {
  createContext,
  type ReactNode,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
} from "react";
import {
  ApiError,
  apiOptions,
  applicationApi,
  invalidResponse,
  isAbortError,
  type PasskeyChallenge,
  readPasskeyChallenge,
  readStaffIdentity,
  readStaffOutcome,
  type StaffCredentialOutcome,
  type StaffFactor,
  type StaffIdentity,
  type StaffTokens,
  staffApi,
  toApiError,
} from "@/shared/api";
import { getPasskeyAssertion } from "@/shared/lib/passkeys";

type SessionStatus = "restoring" | "anonymous" | "authenticating" | "authenticated";
type FactorPresentation = { method: StaffFactor["method"]; canUsePasskey: boolean };
type RefreshOutcome =
  | { kind: "refreshed"; accessToken: string }
  | { kind: "transient"; error: ApiError }
  | { kind: "ended" };
type SessionState = {
  staff: StaffIdentity | null;
  status: SessionStatus;
  factor: FactorPresentation | null;
  passkeyReady: boolean;
  preparingPasskey: boolean;
  error: ApiError | null;
};
type SessionContextValue = SessionState & {
  restoreSession(): Promise<void>;
  synchronizeIdentity(signal: AbortSignal): Promise<void>;
  /** Read-only GET callback; it may run once more after an active-session refresh. */
  request<T>(
    send: (api: ReturnType<typeof applicationApi>, signal: AbortSignal) => Promise<T>,
    signal: AbortSignal,
  ): Promise<T>;
  signInWithPassword(login: string, password: string): Promise<void>;
  preparePasskey(): Promise<void>;
  signInWithPasskey(): Promise<void>;
  completeFactor(code: string): Promise<void>;
  completeFactorWithPasskey(): Promise<void>;
  completeRegistration(
    challenge: string,
    authenticatorCode?: string,
    passkeyResponse?: Record<string, unknown>,
  ): Promise<boolean>;
  cancelSignIn(): void;
  signOut(): Promise<void>;
  clearError(): void;
};

type StaffSessionLifecycleCallbacks = {
  onAuthenticated?: (
    identity: StaffIdentity,
    context: { source: "credential" | "restore"; password?: string },
  ) => void;
  onSessionCleared?: () => void;
  onLogout?: () => void | Promise<void>;
};

const initialState: SessionState = {
  staff: null,
  status: "anonymous",
  factor: null,
  passkeyReady: false,
  preparingPasskey: false,
  error: null,
};
const SessionContext = createContext<SessionContextValue | null>(null);
const credentialFailure =
  "Не удалось войти. Проверьте логин и средство входа или начните вход заново.";

type RestoredSession = { tokens: StaffTokens; staff: StaffIdentity };
let sharedRestoreTask: Promise<RestoredSession> | null = null;

function withSessionCookieLock<T>(operation: () => Promise<T>, signal?: AbortSignal): Promise<T> {
  signal?.throwIfAborted();
  const send = () => {
    signal?.throwIfAborted();
    return operation();
  };
  return navigator.locks?.request
    ? navigator.locks.request(
        "melodytrack.staff.restore",
        { mode: "exclusive", ...(signal ? { signal } : {}) },
        send,
      )
    : send();
}

function restoreFromCookie(): Promise<RestoredSession> {
  if (sharedRestoreTask) return sharedRestoreTask;
  const load = async (): Promise<RestoredSession> => {
    // A subscriber unmount must not interrupt refresh rotation before Set-Cookie is received.
    const controller = new AbortController();
    const outcome = readStaffOutcome(
      await staffApi().staff.restore.post(apiOptions(controller.signal)),
    );
    if (outcome.kind !== "tokens") throw invalidResponse();
    const identity = readStaffIdentity(
      await staffApi(outcome.accessToken).me.get(apiOptions(controller.signal)),
    );
    if (identity.staffId !== outcome.staffId) throw invalidResponse();
    return { tokens: outcome, staff: identity };
  };
  const task = withSessionCookieLock(load);
  sharedRestoreTask = task;
  const settled = () => {
    if (sharedRestoreTask === task) sharedRestoreTask = null;
  };
  void task.then(settled, settled);
  return task;
}

function waitForRefresh<T>(task: Promise<T>, signal: AbortSignal): Promise<T> {
  signal.throwIfAborted();
  return new Promise((resolve, reject) => {
    const aborted = () => reject(new DOMException("Request cancelled", "AbortError"));
    signal.addEventListener("abort", aborted, { once: true });
    task.then(
      (outcome) => {
        signal.removeEventListener("abort", aborted);
        resolve(outcome);
      },
      (error: unknown) => {
        signal.removeEventListener("abort", aborted);
        reject(error);
      },
    );
  });
}

export function StaffSessionProvider({
  children,
  onAuthenticated,
  onSessionCleared,
  onLogout,
}: { children: ReactNode } & StaffSessionLifecycleCallbacks) {
  const [state, setState] = useState<SessionState>({ ...initialState, status: "restoring" });
  // Credential material stays in memory; browser restart recovery uses an HttpOnly server cookie.
  const tokens = useRef<StaffTokens | null>(null);
  const restoring = useRef(true);
  const restoreTask = useRef<Promise<void> | null>(null);
  const restoreAction = useRef<() => Promise<void>>(async () => {});
  const resourceRequests = useRef(new Set<AbortController>());
  const staff = useRef<StaffIdentity | null>(null);
  const factor = useRef<StaffFactor | null>(null);
  const primaryPasskey = useRef<(PasskeyChallenge & { expiresAt: number }) | null>(null);
  const passkeyRequest = useRef<AbortController | null>(null);
  const passkeyTask = useRef<Promise<void> | null>(null);
  const passkeyTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const generation = useRef(0);
  const activeRequest = useRef<AbortController | null>(null);
  const refreshTask = useRef<Promise<RefreshOutcome> | null>(null);
  const lastRefreshOutcome = useRef<RefreshOutcome | null>(null);
  const refreshTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const refreshAction = useRef<() => Promise<RefreshOutcome>>(async () => ({ kind: "ended" }));
  const channel = useRef<BroadcastChannel | null>(null);
  const callbacks = useRef({
    onAuthenticated,
    onSessionCleared,
    onLogout,
  });
  callbacks.current = { onAuthenticated, onSessionCleared, onLogout };
  const pendingPassword = useRef<string | null>(null);
  const pendingPasswordExpiresAt = useRef<number | null>(null);
  const pendingPasswordTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const notify = useCallback((callback: (() => void) | undefined) => {
    try {
      callback?.();
    } catch {
      // Lifecycle observers must not change authentication outcomes.
    }
  }, []);

  const notifyAuthenticated = useCallback(
    (identity: StaffIdentity, context: { source: "credential" | "restore"; password?: string }) => {
      try {
        callbacks.current.onAuthenticated?.(identity, context);
      } catch {
        // Lifecycle observers must not change authentication outcomes.
      }
    },
    [],
  );

  const schedulePasswordExpiry = useCallback((expiresAt: number) => {
    if (pendingPasswordTimer.current !== null) clearTimeout(pendingPasswordTimer.current);
    pendingPasswordExpiresAt.current = expiresAt;
    pendingPasswordTimer.current = setTimeout(
      () => {
        pendingPassword.current = null;
        pendingPasswordExpiresAt.current = null;
        pendingPasswordTimer.current = null;
      },
      Math.max(0, expiresAt - Date.now()),
    );
  }, []);

  const stopTimer = useCallback(() => {
    if (refreshTimer.current !== null) clearTimeout(refreshTimer.current);
    refreshTimer.current = null;
  }, []);

  const stopPasskeyPreparation = useCallback(() => {
    passkeyRequest.current?.abort();
    passkeyRequest.current = null;
    passkeyTask.current = null;
    primaryPasskey.current = null;
    if (passkeyTimer.current !== null) clearTimeout(passkeyTimer.current);
    passkeyTimer.current = null;
  }, []);

  const clearLocal = useCallback(
    (error: ApiError | null = null) => {
      generation.current += 1;
      for (const request of resourceRequests.current) request.abort();
      resourceRequests.current.clear();
      activeRequest.current?.abort();
      activeRequest.current = null;
      restoreTask.current = null;
      restoring.current = false;
      refreshTask.current = null;
      lastRefreshOutcome.current = null;
      stopTimer();
      stopPasskeyPreparation();
      tokens.current = null;
      staff.current = null;
      factor.current = null;
      primaryPasskey.current = null;
      pendingPassword.current = null;
      pendingPasswordExpiresAt.current = null;
      if (pendingPasswordTimer.current !== null) clearTimeout(pendingPasswordTimer.current);
      pendingPasswordTimer.current = null;
      setState({ ...initialState, error });
      notify(callbacks.current.onSessionCleared);
    },
    [notify, stopTimer, stopPasskeyPreparation],
  );

  const scheduleRefresh = useCallback(
    (expiresIn: number) => {
      stopTimer();
      refreshTimer.current = setTimeout(
        () => {
          void refreshAction.current();
        },
        Math.max(1000, Math.min(expiresIn * 1000 - 30_000, 270_000)),
      );
    },
    [stopTimer],
  );

  const restoreSession = useCallback((): Promise<void> => {
    if (restoreTask.current) return restoreTask.current;
    if (staff.current || factor.current || (activeRequest.current && !restoring.current))
      return Promise.resolve();
    generation.current += 1;
    activeRequest.current?.abort();
    stopTimer();
    stopPasskeyPreparation();
    const id = generation.current;
    const controller = new AbortController();
    activeRequest.current = controller;
    restoring.current = true;
    setState({ ...initialState, status: "restoring" });
    const run = async (): Promise<void> => {
      try {
        const restored = await waitForRefresh(restoreFromCookie(), controller.signal);
        if (id !== generation.current || controller.signal.aborted) return;
        tokens.current = restored.tokens;
        staff.current = restored.staff;
        notifyAuthenticated(restored.staff, { source: "restore" });
        restoring.current = false;
        setState({ ...initialState, staff: restored.staff, status: "authenticated" });
        scheduleRefresh(restored.tokens.expiresIn);
      } catch (error) {
        if (id !== generation.current || isAbortError(error)) return;
        const apiError = toApiError(error);
        if (apiError.status === 401) {
          clearLocal();
        } else if (apiError.transient) {
          setState({ ...initialState, status: "restoring", error: apiError });
        } else {
          clearLocal(apiError);
        }
      } finally {
        if (activeRequest.current === controller) activeRequest.current = null;
        if (restoreTask.current === task) restoreTask.current = null;
      }
    };
    const task = run();
    restoreTask.current = task;
    return task;
  }, [clearLocal, notifyAuthenticated, scheduleRefresh, stopPasskeyPreparation, stopTimer]);

  useEffect(() => {
    restoreAction.current = restoreSession;
  }, [restoreSession]);

  const beginSignIn = useCallback(
    (presentation: FactorPresentation | null = null, preservePendingPassword = false) => {
      const password = preservePendingPassword ? pendingPassword.current : null;
      const passwordExpiresAt = preservePendingPassword ? pendingPasswordExpiresAt.current : null;
      clearLocal();
      if (password && passwordExpiresAt && passwordExpiresAt > Date.now()) {
        pendingPassword.current = password;
        schedulePasswordExpiry(passwordExpiresAt);
      }
      const controller = new AbortController();
      activeRequest.current = controller;
      setState({ ...initialState, status: "authenticating", factor: presentation });
      return { controller, id: generation.current };
    },
    [clearLocal, schedulePasswordExpiry],
  );

  const acceptOutcome = useCallback(
    async (outcome: StaffCredentialOutcome, id: number, controller: AbortController) => {
      if (id !== generation.current) return;
      if (outcome.kind === "factor") {
        factor.current = outcome;
        setState({
          ...initialState,
          factor: { method: outcome.method, canUsePasskey: Boolean(outcome.optionsJson) },
        });
        return;
      }
      // The completed credential flow already switched the server browser context,
      // even if the following identity read encounters a connectivity failure.
      channel.current?.postMessage("changed");
      const identity = readStaffIdentity(
        await staffApi(outcome.accessToken).me.get(apiOptions(controller.signal)),
      );
      if (identity.staffId !== outcome.staffId) throw invalidResponse();
      if (id !== generation.current) return;
      tokens.current = outcome;
      staff.current = identity;
      factor.current = null;
      primaryPasskey.current = null;
      setState({ ...initialState, staff: identity, status: "authenticated" });
      scheduleRefresh(outcome.expiresIn);
      const password = pendingPassword.current;
      pendingPassword.current = null;
      pendingPasswordExpiresAt.current = null;
      if (pendingPasswordTimer.current !== null) clearTimeout(pendingPasswordTimer.current);
      pendingPasswordTimer.current = null;
      notifyAuthenticated(identity, { source: "credential", ...(password ? { password } : {}) });
    },
    [notifyAuthenticated, scheduleRefresh],
  );

  const failSignIn = useCallback(
    (error: unknown, id: number) => {
      if (id !== generation.current) return;
      if (isAbortError(error)) {
        clearLocal();
        return;
      }
      clearLocal(toApiError(error));
    },
    [clearLocal],
  );

  const signInWithPassword = useCallback(
    async (login: string, password: string) => {
      const { controller, id } = beginSignIn();
      pendingPassword.current = password;
      schedulePasswordExpiry(Date.now() + 5 * 60_000);
      try {
        const outcome = readStaffOutcome(
          await withSessionCookieLock(
            () =>
              staffApi().staff.password.post(
                { login, password },
                apiOptions(controller.signal, credentialFailure),
              ),
            controller.signal,
          ),
        );
        await acceptOutcome(outcome, id, controller);
      } catch (error) {
        failSignIn(error, id);
      }
    },
    [acceptOutcome, beginSignIn, failSignIn, schedulePasswordExpiry],
  );

  const completeRegistration = useCallback(
    async (
      challenge: string,
      authenticatorCode?: string,
      passkeyResponse?: Record<string, unknown>,
    ): Promise<boolean> => {
      const { controller, id } = beginSignIn();
      try {
        const outcome = readStaffOutcome(
          await withSessionCookieLock(
            () =>
              staffApi().staff.register.complete.post(
                {
                  challenge,
                  ...(authenticatorCode ? { authenticatorCode } : {}),
                  ...(passkeyResponse
                    ? { passkeyResponse: { additionalData: passkeyResponse } }
                    : {}),
                },
                apiOptions(
                  controller.signal,
                  "Не удалось завершить регистрацию. Настройте вход заново или войдите, если аккаунт уже создан.",
                ),
              ),
            controller.signal,
          ),
        );
        await acceptOutcome(outcome, id, controller);
        return id === generation.current;
      } catch (error) {
        failSignIn(error, id);
        return false;
      }
    },
    [acceptOutcome, beginSignIn, failSignIn],
  );

  const preparePasskey = useCallback(function prepare(): Promise<void> {
    if (restoring.current || staff.current || factor.current || activeRequest.current)
      return Promise.resolve();
    if (primaryPasskey.current && primaryPasskey.current.expiresAt > Date.now())
      return Promise.resolve();
    if (passkeyTask.current) return passkeyTask.current;
    const controller = new AbortController();
    passkeyRequest.current = controller;
    const id = generation.current;
    setState((previous) => ({ ...previous, preparingPasskey: true, passkeyReady: false }));
    const task = (async () => {
      try {
        const challenge = readPasskeyChallenge(
          await staffApi().staff.passkey.optionsPath.post(apiOptions(controller.signal)),
        );
        if (id !== generation.current || controller.signal.aborted) return;
        primaryPasskey.current = { ...challenge, expiresAt: Date.now() + 240_000 };
        setState((previous) => ({ ...previous, preparingPasskey: false, passkeyReady: true }));
        if (passkeyTimer.current !== null) clearTimeout(passkeyTimer.current);
        passkeyTimer.current = setTimeout(() => {
          passkeyTimer.current = null;
          primaryPasskey.current = null;
          setState((previous) => ({ ...previous, passkeyReady: false }));
          if (document.visibilityState === "visible") void prepare();
        }, 240_000);
      } catch (error) {
        if (id !== generation.current || isAbortError(error)) return;
        setState((previous) => ({
          ...previous,
          preparingPasskey: false,
          passkeyReady: false,
          error: toApiError(error),
        }));
      } finally {
        if (passkeyRequest.current === controller) {
          passkeyRequest.current = null;
          passkeyTask.current = null;
        }
      }
    })();
    passkeyTask.current = task;
    return task;
  }, []);

  const signInWithPasskey = useCallback(async () => {
    const prepared = primaryPasskey.current;
    if (!prepared || prepared.expiresAt <= Date.now()) {
      void preparePasskey();
      return;
    }
    const { controller, id } = beginSignIn();
    try {
      const response = await getPasskeyAssertion(prepared.optionsJson, controller.signal);
      const outcome = readStaffOutcome(
        await withSessionCookieLock(
          () =>
            staffApi().staff.passkey.complete.post(
              { challenge: prepared.challenge, response: { additionalData: response } },
              apiOptions(controller.signal, credentialFailure),
            ),
          controller.signal,
        ),
      );
      await acceptOutcome(outcome, id, controller);
    } catch (error) {
      failSignIn(error, id);
    }
  }, [acceptOutcome, beginSignIn, failSignIn, preparePasskey]);

  const completeFactor = useCallback(
    async (code: string) => {
      const pending = factor.current;
      if (!pending) return;
      const { controller, id } = beginSignIn(
        {
          method: pending.method,
          canUsePasskey: Boolean(pending.optionsJson),
        },
        true,
      );
      try {
        const outcome = readStaffOutcome(
          await withSessionCookieLock(
            () =>
              staffApi().staff.factor.post(
                { challenge: pending.challenge, code },
                apiOptions(controller.signal, "Код не подошёл. Начните вход заново."),
              ),
            controller.signal,
          ),
        );
        await acceptOutcome(outcome, id, controller);
      } catch (error) {
        failSignIn(error, id);
      }
    },
    [acceptOutcome, beginSignIn, failSignIn],
  );

  const completeFactorWithPasskey = useCallback(async () => {
    const pending = factor.current;
    if (!pending?.optionsJson || pending.method !== "authenticatorOrPasskey") return;
    const { controller, id } = beginSignIn(
      {
        method: pending.method,
        canUsePasskey: Boolean(pending.optionsJson),
      },
      true,
    );
    try {
      const passkeyResponse = await getPasskeyAssertion(pending.optionsJson, controller.signal);
      const outcome = readStaffOutcome(
        await withSessionCookieLock(
          () =>
            staffApi().staff.factor.post(
              {
                challenge: pending.challenge,
                passkeyResponse: { additionalData: passkeyResponse },
              },
              apiOptions(controller.signal, "Не удалось подтвердить вход. Начните заново."),
            ),
          controller.signal,
        ),
      );
      await acceptOutcome(outcome, id, controller);
    } catch (error) {
      failSignIn(error, id);
    }
  }, [acceptOutcome, beginSignIn, failSignIn]);

  const refreshSession = useCallback((): Promise<RefreshOutcome> => {
    if (refreshTask.current) return refreshTask.current;
    const previousTokens = tokens.current;
    if (!previousTokens || !staff.current) return Promise.resolve({ kind: "ended" });
    const id = generation.current;
    const controller = new AbortController();
    activeRequest.current = controller;
    lastRefreshOutcome.current = null;
    stopTimer();
    const run = async (): Promise<RefreshOutcome> => {
      try {
        const restored = await waitForRefresh(restoreFromCookie(), controller.signal);
        if (id !== generation.current) return { kind: "ended" };
        const outcome = restored.tokens;
        const identity = restored.staff;
        if (identity.staffId !== previousTokens.staffId || identity.staffId !== outcome.staffId)
          throw invalidResponse();
        tokens.current = outcome;
        staff.current = identity;
        setState({ ...initialState, staff: identity, status: "authenticated" });
        scheduleRefresh(outcome.expiresIn);
        const refreshed: RefreshOutcome = { kind: "refreshed", accessToken: outcome.accessToken };
        lastRefreshOutcome.current = refreshed;
        return refreshed;
      } catch (error) {
        if (id !== generation.current || isAbortError(error)) return { kind: "ended" };
        const apiError = toApiError(error);
        if (apiError.transient) {
          // Loaded account data remains owned by the same staff member while offline.
          // Retry only on connectivity/focus events; no API polling is introduced.
          setState((current) => ({ ...current, error: apiError }));
          const unavailable: RefreshOutcome = { kind: "transient", error: apiError };
          lastRefreshOutcome.current = unavailable;
          return unavailable;
        } else {
          clearLocal(apiError);
          return { kind: "ended" };
        }
      } finally {
        if (id === generation.current) refreshTask.current = null;
      }
    };
    const task = run();
    refreshTask.current = task;
    return task;
  }, [clearLocal, scheduleRefresh, stopTimer]);

  useEffect(() => {
    refreshAction.current = refreshSession;
  }, [refreshSession]);

  const signOut = useCallback(async () => {
    try {
      await callbacks.current.onLogout?.();
    } catch {
      const error = new ApiError("Не удалось закрыть локальные данные. Повторите выход.");
      setState((value) => ({ ...value, error }));
      throw error;
    }
    const current = tokens.current;
    if (!current) {
      clearLocal();
      return;
    }
    generation.current += 1;
    activeRequest.current?.abort();
    refreshTask.current = null;
    stopTimer();
    const id = generation.current;
    const controller = new AbortController();
    activeRequest.current = controller;
    setState((value) => ({ ...value, status: "authenticating", error: null }));
    try {
      await withSessionCookieLock(
        () => staffApi(current.accessToken).logout.post(apiOptions(controller.signal)),
        controller.signal,
      );
      if (id !== generation.current) return;
      clearLocal();
      channel.current?.postMessage("changed");
    } catch (error) {
      if (id !== generation.current || isAbortError(error)) return;
      const apiError = toApiError(error);
      if (apiError.status === 401 || apiError.status === 403) {
        clearLocal(apiError);
      } else {
        setState((value) => ({
          ...value,
          status: "authenticated",
          error: new ApiError(
            "Не удалось выйти. Повторите попытку.",
            apiError.status,
            apiError.traceId,
          ),
        }));
        scheduleRefresh(current.expiresIn);
      }
    }
  }, [clearLocal, scheduleRefresh, stopTimer]);

  useEffect(() => {
    const revalidate = () => {
      if (document.visibilityState !== "visible" || !navigator.onLine) return;
      if (restoring.current) void restoreAction.current();
      else void refreshAction.current();
    };
    let updates: BroadcastChannel | null = null;
    try {
      if (typeof BroadcastChannel !== "undefined") {
        updates = new BroadcastChannel("melodytrack-rewrite-account");
        channel.current = updates;
        updates.onmessage = (event: MessageEvent<unknown>) => {
          if (event.data === "changed")
            clearLocal(
              new ApiError("Активный аккаунт изменился в другой вкладке. Войдите снова.", 401),
            );
        };
      }
    } catch {
      /* Focus revalidation is also available when a browser disallows channels. */
    }
    window.addEventListener("focus", revalidate);
    window.addEventListener("online", revalidate);
    document.addEventListener("visibilitychange", revalidate);
    return () => {
      generation.current += 1;
      for (const request of resourceRequests.current) request.abort();
      resourceRequests.current.clear();
      activeRequest.current?.abort();
      restoreTask.current = null;
      refreshTask.current = null;
      stopTimer();
      stopPasskeyPreparation();
      pendingPassword.current = null;
      pendingPasswordExpiresAt.current = null;
      if (pendingPasswordTimer.current !== null) clearTimeout(pendingPasswordTimer.current);
      pendingPasswordTimer.current = null;
      notify(callbacks.current.onSessionCleared);
      updates?.close();
      channel.current = null;
      window.removeEventListener("focus", revalidate);
      window.removeEventListener("online", revalidate);
      document.removeEventListener("visibilitychange", revalidate);
    };
  }, [clearLocal, notify, stopTimer, stopPasskeyPreparation]);

  useEffect(() => {
    void restoreSession();
  }, [restoreSession]);

  const request = useCallback(
    async <T,>(
      send: (api: ReturnType<typeof applicationApi>, signal: AbortSignal) => Promise<T>,
      signal: AbortSignal,
    ): Promise<T> => {
      signal.throwIfAborted();
      const accountId = staff.current?.staffId;
      if (!tokens.current || !accountId)
        throw new ApiError("Сессия завершена. Войдите снова.", 401);
      const id = generation.current;
      const controller = new AbortController();
      resourceRequests.current.add(controller);
      const combined = AbortSignal.any([signal, controller.signal]);
      const ensureAccount = () => {
        combined.throwIfAborted();
        if (id !== generation.current || staff.current?.staffId !== accountId)
          throw new DOMException("Session changed", "AbortError");
      };
      const wait = async (task: Promise<RefreshOutcome>) => {
        const outcome = await waitForRefresh(task, combined);
        ensureAccount();
        if (outcome.kind === "transient") throw outcome.error;
        return outcome;
      };
      try {
        if (refreshTask.current) await wait(refreshTask.current);
        ensureAccount();
        const current = tokens.current;
        if (!current) throw new DOMException("Session changed", "AbortError");
        try {
          const result = await send(applicationApi(current.accessToken), combined);
          ensureAccount();
          return result;
        } catch (error) {
          ensureAccount();
          if (toApiError(error).status !== 401) throw error;
          let refreshed: RefreshOutcome;
          if (refreshTask.current) {
            refreshed = await wait(refreshTask.current);
          } else if (lastRefreshOutcome.current?.kind === "transient") {
            // Do not turn failed refreshes into a retry loop on each resource request.
            throw lastRefreshOutcome.current.error;
          } else if (
            lastRefreshOutcome.current?.kind === "refreshed" &&
            lastRefreshOutcome.current.accessToken === tokens.current?.accessToken &&
            tokens.current.accessToken !== current.accessToken
          ) {
            refreshed = lastRefreshOutcome.current;
          } else {
            refreshed = await wait(refreshSession());
          }
          ensureAccount();
          const rotated = tokens.current;
          if (
            refreshed.kind !== "refreshed" ||
            !rotated ||
            rotated.staffId !== accountId ||
            rotated.accessToken === current.accessToken ||
            rotated.accessToken !== refreshed.accessToken
          ) {
            const failure = invalidResponse();
            clearLocal(failure);
            throw failure;
          }
          const result = await send(applicationApi(rotated.accessToken), combined);
          ensureAccount();
          return result;
        }
      } catch (error) {
        if (!combined.aborted && id === generation.current && toApiError(error).status === 401)
          clearLocal(toApiError(error));
        throw error;
      } finally {
        resourceRequests.current.delete(controller);
      }
    },
    [clearLocal, refreshSession],
  );

  const synchronizeIdentity = useCallback(
    async (signal: AbortSignal) => {
      const id = generation.current;
      const identity = await request(
        async (api, requestSignal) =>
          readStaffIdentity(await api.auth.me.get(apiOptions(requestSignal))),
        signal,
      );
      signal.throwIfAborted();
      if (id !== generation.current) throw new DOMException("Session changed", "AbortError");
      if (staff.current?.staffId !== identity.staffId) {
        clearLocal(invalidResponse());
        return;
      }
      staff.current = identity;
      setState((current) => ({ ...current, staff: identity }));
    },
    [request, clearLocal],
  );

  return (
    <SessionContext.Provider
      value={{
        ...state,
        restoreSession,
        synchronizeIdentity,
        request,
        signInWithPassword,
        preparePasskey,
        signInWithPasskey,
        completeFactor,
        completeFactorWithPasskey,
        completeRegistration,
        cancelSignIn: clearLocal,
        signOut,
        clearError: () => setState((value) => ({ ...value, error: null })),
      }}
    >
      {children}
    </SessionContext.Provider>
  );
}

export function useStaffSession(): SessionContextValue {
  const value = useContext(SessionContext);
  if (!value) throw new Error("StaffSessionProvider is required.");
  return value;
}
