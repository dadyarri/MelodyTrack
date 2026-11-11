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

type SessionStatus = "anonymous" | "authenticating" | "authenticated";
type FactorPresentation = { method: StaffFactor["method"]; canUsePasskey: boolean };
type SessionState = {
  staff: StaffIdentity | null;
  status: SessionStatus;
  factor: FactorPresentation | null;
  passkeyReady: boolean;
  preparingPasskey: boolean;
  error: ApiError | null;
};
type SessionContextValue = SessionState & {
  signInWithPassword(login: string, password: string): Promise<void>;
  preparePasskey(): Promise<void>;
  signInWithPasskey(): Promise<void>;
  completeFactor(code: string): Promise<void>;
  completeFactorWithPasskey(): Promise<void>;
  completeRegistration(challenge: string, authenticatorCode?: string, passkeyResponse?: Record<string, unknown>): Promise<boolean>;
  cancelSignIn(): void;
  signOut(): Promise<void>;
  clearError(): void;
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

export function StaffSessionProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<SessionState>(initialState);
  // All credentials and challenges belong to this mounted tab; none are persisted or broadcast.
  const tokens = useRef<StaffTokens | null>(null);
  const staff = useRef<StaffIdentity | null>(null);
  const factor = useRef<StaffFactor | null>(null);
  const primaryPasskey = useRef<(PasskeyChallenge & { expiresAt: number }) | null>(null);
  const passkeyRequest = useRef<AbortController | null>(null);
  const passkeyTask = useRef<Promise<void> | null>(null);
  const passkeyTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const generation = useRef(0);
  const activeRequest = useRef<AbortController | null>(null);
  const refreshTask = useRef<Promise<void> | null>(null);
  const refreshTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const refreshAction = useRef<() => Promise<void>>(async () => {});
  const channel = useRef<BroadcastChannel | null>(null);

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
      activeRequest.current?.abort();
      activeRequest.current = null;
      refreshTask.current = null;
      stopTimer();
      stopPasskeyPreparation();
      tokens.current = null;
      staff.current = null;
      factor.current = null;
      primaryPasskey.current = null;
      setState({ ...initialState, error });
    },
    [stopTimer, stopPasskeyPreparation],
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

  const beginSignIn = useCallback(
    (presentation: FactorPresentation | null = null) => {
      clearLocal();
      const controller = new AbortController();
      activeRequest.current = controller;
      setState({ ...initialState, status: "authenticating", factor: presentation });
      return { controller, id: generation.current };
    },
    [clearLocal],
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
    },
    [scheduleRefresh],
  );

  const failSignIn = useCallback(
    (error: unknown, id: number) => {
      if (id !== generation.current || isAbortError(error)) return;
      clearLocal(toApiError(error));
    },
    [clearLocal],
  );

  const signInWithPassword = useCallback(
    async (login: string, password: string) => {
      const { controller, id } = beginSignIn();
      try {
        const outcome = readStaffOutcome(
          await staffApi().staff.password.post(
            { login, password },
            apiOptions(controller.signal, credentialFailure),
          ),
        );
        await acceptOutcome(outcome, id, controller);
      } catch (error) {
        failSignIn(error, id);
      }
    },
    [acceptOutcome, beginSignIn, failSignIn],
  );

  const completeRegistration = useCallback(
    async (challenge: string, authenticatorCode?: string, passkeyResponse?: Record<string, unknown>): Promise<boolean> => {
      const { controller, id } = beginSignIn();
      try {
        const outcome = readStaffOutcome(await staffApi().staff.register.complete.post({
          challenge,
          ...(authenticatorCode ? { authenticatorCode } : {}),
          ...(passkeyResponse ? { passkeyResponse: { additionalData: passkeyResponse } } : {}),
        }, apiOptions(controller.signal, "Не удалось завершить регистрацию. Настройте вход заново или войдите, если аккаунт уже создан.")));
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
    if (staff.current || factor.current || activeRequest.current) return Promise.resolve();
    if (primaryPasskey.current && primaryPasskey.current.expiresAt > Date.now()) return Promise.resolve();
    if (passkeyTask.current) return passkeyTask.current;
    const controller = new AbortController();
    passkeyRequest.current = controller;
    const id = generation.current;
    setState((previous) => ({ ...previous, preparingPasskey: true, passkeyReady: false }));
    const task = (async () => {
      try {
        const challenge = readPasskeyChallenge(await staffApi().staff.passkey.optionsPath.post(apiOptions(controller.signal)));
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
        setState((previous) => ({ ...previous, preparingPasskey: false, passkeyReady: false, error: toApiError(error) }));
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

  const signInWithPasskey = useCallback(
    async () => {
      const prepared = primaryPasskey.current;
      if (!prepared || prepared.expiresAt <= Date.now()) {
        void preparePasskey();
        return;
      }
      const { controller, id } = beginSignIn();
      try {
        const response = await getPasskeyAssertion(prepared.optionsJson, controller.signal);
        const outcome = readStaffOutcome(
          await staffApi().staff.passkey.complete.post(
            { challenge: prepared.challenge, response: { additionalData: response } },
            apiOptions(controller.signal, credentialFailure),
          ),
        );
        await acceptOutcome(outcome, id, controller);
      } catch (error) {
        failSignIn(error, id);
      }
    },
    [acceptOutcome, beginSignIn, failSignIn, preparePasskey],
  );

  const completeFactor = useCallback(
    async (code: string) => {
      const pending = factor.current;
      if (!pending) return;
      const { controller, id } = beginSignIn({
        method: pending.method,
        canUsePasskey: Boolean(pending.optionsJson),
      });
      try {
        const outcome = readStaffOutcome(
          await staffApi().staff.factor.post(
            { challenge: pending.challenge, code },
            apiOptions(controller.signal, "Код не подошёл. Начните вход заново."),
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
    const { controller, id } = beginSignIn({
      method: pending.method,
      canUsePasskey: Boolean(pending.optionsJson),
    });
    try {
      const passkeyResponse = await getPasskeyAssertion(pending.optionsJson, controller.signal);
      const outcome = readStaffOutcome(
        await staffApi().staff.factor.post(
          { challenge: pending.challenge, passkeyResponse: { additionalData: passkeyResponse } },
          apiOptions(controller.signal, "Не удалось подтвердить вход. Начните заново."),
        ),
      );
      await acceptOutcome(outcome, id, controller);
    } catch (error) {
      failSignIn(error, id);
    }
  }, [acceptOutcome, beginSignIn, failSignIn]);

  const refreshSession = useCallback((): Promise<void> => {
    if (refreshTask.current) return refreshTask.current;
    const previousTokens = tokens.current;
    if (!previousTokens || !staff.current) return Promise.resolve();
    const id = generation.current;
    const controller = new AbortController();
    activeRequest.current = controller;
    stopTimer();
    const run = async () => {
      try {
        const outcome = readStaffOutcome(
          await staffApi().staff.refresh.post(
            { refreshToken: previousTokens.refreshToken },
            apiOptions(controller.signal),
          ),
        );
        if (outcome.kind !== "tokens") throw invalidResponse();
        if (id !== generation.current) return;
        // Preserve the rotated refresh even if the subsequent identity read loses the connection.
        tokens.current = outcome;
        const identity = readStaffIdentity(
          await staffApi(outcome.accessToken).me.get(apiOptions(controller.signal)),
        );
        if (identity.staffId !== previousTokens.staffId || identity.staffId !== outcome.staffId)
          throw invalidResponse();
        if (id !== generation.current) return;
        staff.current = identity;
        setState({ ...initialState, staff: identity, status: "authenticated" });
        scheduleRefresh(outcome.expiresIn);
      } catch (error) {
        if (id !== generation.current || isAbortError(error)) return;
        const apiError = toApiError(error);
        if (apiError.status === 0) {
          // Loaded account data remains owned by the same staff member while offline.
          // Retry only on connectivity/focus events; no API polling is introduced.
          setState((current) => ({ ...current, error: apiError }));
        } else {
          clearLocal(apiError);
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
      await staffApi(current.accessToken).logout.post(apiOptions(controller.signal));
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
      if (document.visibilityState === "visible" && navigator.onLine) void refreshAction.current();
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
      activeRequest.current?.abort();
      refreshTask.current = null;
      stopTimer();
      stopPasskeyPreparation();
      updates?.close();
      channel.current = null;
      window.removeEventListener("focus", revalidate);
      window.removeEventListener("online", revalidate);
      document.removeEventListener("visibilitychange", revalidate);
    };
  }, [clearLocal, stopTimer, stopPasskeyPreparation]);

  return (
    <SessionContext.Provider
      value={{
        ...state,
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
