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
  createOfflineVault,
  deleteOfflineVault,
  hasOfflineVault,
  isOfflineVaultBlocked,
  markOfflineVaultForDeletion,
  needsOfflineVaultDeletion,
  type OfflineVault,
  OfflineVaultAlreadyExistsError,
  setOfflineVaultBlocked,
  unlockOfflineVault,
} from "@/shared/offline";

type Status = "idle" | "checking" | "missing" | "locked" | "unlocked" | "blocked" | "error";
type State = { staffId: string | null; status: Status; error: string | null };
type Authentication = { source: "credential" | "restore"; password?: string };
export type OfflineStorageContextValue = State & {
  authenticate(staffId: string, context: Authentication): void;
  clear(): void;
  logout(): Promise<void>;
  unlock(secret: string): Promise<void>;
  get(id: string): Promise<unknown | undefined>;
  put(
    id: string,
    value: unknown,
    kind: "pending" | "cache" | "applied" | "conflict",
  ): Promise<void>;
  remove(id: string): Promise<void>;
  revokeClientAccess(): Promise<void>;
};
const Context = createContext<OfflineStorageContextValue | null>(null);
const blockedKey = (staffId: string) => `melodytrack.offline.blocked:${staffId}`;
const closedMessage = "Локальные данные закрыты после выхода. Войдите в аккаунт заново.";

export function OfflineStorageProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<State>({ staffId: null, status: "idle", error: null });
  const owner = useRef<string | null>(null);
  const vault = useRef<OfflineVault | null>(null);
  const generation = useRef(0);
  const channel = useRef<BroadcastChannel | null>(null);
  const revoked = useRef(false);
  const opening = useRef<Promise<void> | null>(null);
  const cleanupTask = useRef<Promise<void>>(Promise.resolve());
  const pendingPurges = useRef(new Set<string>());

  const lock = useCallback(() => {
    generation.current += 1;
    vault.current?.lock();
    vault.current = null;
  }, []);
  const clear = useCallback(() => {
    lock();
    owner.current = null;
    setState({ staffId: null, status: "idle", error: null });
  }, [lock]);
  const block = useCallback(() => {
    lock();
    setState({ staffId: owner.current, status: "blocked", error: closedMessage });
  }, [lock]);
  const logout = useCallback(async () => {
    const staffId = owner.current;
    if (!staffId) return;
    // A persistent marker also covers sleeping tabs and a browser restart.
    let saved = false;
    try {
      localStorage.setItem(blockedKey(staffId), "1");
      saved = true;
    } catch {
      /* IndexedDB is the independent durable fallback. */
    }
    block();
    try {
      channel.current?.postMessage({ kind: "logout", staffId });
    } catch {
      /* Durable markers remain authoritative. */
    }
    try {
      await setOfflineVaultBlocked(staffId, true);
    } catch {
      if (!saved) throw new Error("Local logout could not be saved");
    }
  }, [block]);

  const open = useCallback(async (staffId: string, secret: string, id: number) => {
    let opened: OfflineVault | null = null;
    try {
      if (!secret) throw new Error("Missing unlock secret");
      const exists = await hasOfflineVault(staffId);
      try {
        opened = exists
          ? await unlockOfflineVault({ staffId, secret })
          : await createOfflineVault({ staffId, secret });
      } catch (error) {
        if (!(error instanceof OfflineVaultAlreadyExistsError)) throw error;
        opened = await unlockOfflineVault({ staffId, secret });
      }
      if (id !== generation.current || owner.current !== staffId || revoked.current) {
        opened.lock();
        return;
      }
      await opened.prune();
      if (id !== generation.current || owner.current !== staffId || revoked.current) {
        opened.lock();
        return;
      }
      vault.current = opened;
      setState({ staffId, status: "unlocked", error: null });
    } catch {
      opened?.lock();
      if (id === generation.current && owner.current === staffId)
        setState({
          staffId,
          status: "locked",
          error:
            "Не удалось открыть локальные данные. Проверьте пароль или ключ. Существующие данные сохранены.",
        });
    }
  }, []);

  const authenticate = useCallback(
    (staffId: string, context: Authentication) => {
      lock();
      owner.current = staffId;
      revoked.current = false;
      const id = generation.current;
      setState({ staffId, status: "checking", error: null });
      const run = async () => {
        try {
          await cleanupTask.current;
          if (id !== generation.current || owner.current !== staffId) return;
          if (pendingPurges.current.has(staffId) || (await needsOfflineVaultDeletion(staffId))) {
            await deleteOfflineVault(staffId);
            pendingPurges.current.delete(staffId);
            if (id !== generation.current || owner.current !== staffId) return;
          }
          if (context.source === "credential") {
            await setOfflineVaultBlocked(staffId, false);
            if (id !== generation.current || owner.current !== staffId) return;
            localStorage.removeItem(blockedKey(staffId));
          } else {
            const blocked =
              localStorage.getItem(blockedKey(staffId)) || (await isOfflineVaultBlocked(staffId));
            if (id !== generation.current || owner.current !== staffId) return;
            if (blocked) {
              block();
              return;
            }
          }
          if (context.password) {
            const task = open(staffId, context.password, id);
            opening.current = task;
            await task;
            if (opening.current === task) opening.current = null;
            return;
          }
          const exists = await hasOfflineVault(staffId);
          if (id === generation.current && owner.current === staffId)
            setState({ staffId, status: exists ? "locked" : "missing", error: null });
        } catch {
          if (id === generation.current)
            setState({
              staffId,
              status: "error",
              error: "Локальное хранилище недоступно. Работа с сервером продолжается.",
            });
        }
      };
      void run();
    },
    [block, lock, open],
  );

  const unlock = useCallback(
    async (secret: string) => {
      const staffId = owner.current;
      if (!staffId || revoked.current) return;
      const start = generation.current;
      try {
        const blocked =
          localStorage.getItem(blockedKey(staffId)) || (await isOfflineVaultBlocked(staffId));
        if (start !== generation.current || owner.current !== staffId) return;
        if (blocked) {
          block();
          return;
        }
      } catch {
        block();
        return;
      }
      lock();
      const id = generation.current;
      setState({ staffId, status: "checking", error: null });
      const task = open(staffId, secret, id);
      opening.current = task;
      await task;
      if (opening.current === task) opening.current = null;
    },
    [block, lock, open],
  );

  const accessEpoch = generation.current;
  const active = useCallback(
    (expectedOwner: string | null, expectedEpoch: number) => {
      if (
        !vault.current ||
        !owner.current ||
        owner.current !== expectedOwner ||
        generation.current !== expectedEpoch ||
        revoked.current
      )
        throw new Error("Local storage is locked");
      try {
        if (localStorage.getItem(blockedKey(owner.current)))
          throw new Error("Local storage is locked");
      } catch {
        block();
        throw new Error("Local storage is locked");
      }
      return vault.current;
    },
    [block],
  );
  const get = useCallback(
    async (id: string) => {
      const current = active(state.staffId, accessEpoch);
      const epoch = generation.current;
      const record = await current.get<unknown>(id);
      if (epoch !== generation.current) throw new DOMException("Account changed", "AbortError");
      return record?.value;
    },
    [active, state.staffId, accessEpoch],
  );
  const put = useCallback(
    async (id: string, value: unknown, kind: "pending" | "cache" | "applied" | "conflict") => {
      const current = active(state.staffId, accessEpoch);
      const epoch = generation.current;
      await current.put({ id, value, kind, ...(kind === "applied" ? { sentAt: Date.now() } : {}) });
      if (epoch !== generation.current) throw new DOMException("Account changed", "AbortError");
    },
    [active, state.staffId, accessEpoch],
  );
  const remove = useCallback(
    async (id: string) => {
      await active(state.staffId, accessEpoch).delete(id);
    },
    [active, state.staffId, accessEpoch],
  );
  const revokeClientAccess = useCallback((): Promise<void> => {
    const staffId = owner.current;
    if (!staffId) return Promise.resolve();
    revoked.current = true;
    pendingPurges.current.add(staffId);
    block();
    const pendingOpen = opening.current;
    const task = cleanupTask.current
      .then(async () => {
        await pendingOpen;
        try {
          await markOfflineVaultForDeletion(staffId);
        } catch {
          /* Still attempt the atomic deletion if marking fails. */
        }
        // This first vault contains only new-client data, all covered by the revoked grant.
        await deleteOfflineVault(staffId);
        pendingPurges.current.delete(staffId);
      })
      .catch(() => {
        if (owner.current === staffId)
          setState({
            staffId,
            status: "error",
            error: "Доступ отозван. Не удалось очистить локальные данные; повторите подключение.",
          });
      });
    cleanupTask.current = task;
    return task;
  }, [block]);

  useEffect(() => {
    const check = () => {
      const staffId = owner.current;
      if (!staffId) return;
      if (pendingPurges.current.has(staffId)) {
        void revokeClientAccess();
        return;
      }
      try {
        if (localStorage.getItem(blockedKey(staffId))) block();
      } catch {
        block();
      }
      const epoch = generation.current;
      void isOfflineVaultBlocked(staffId)
        .then((blocked) => {
          if (blocked && epoch === generation.current && owner.current === staffId) block();
        })
        .catch(() => {
          if (epoch === generation.current) block();
        });
    };
    const changed = (event: StorageEvent) => {
      if (event.key === null || event.key === (owner.current ? blockedKey(owner.current) : ""))
        check();
    };
    let updates: BroadcastChannel | null = null;
    try {
      updates = new BroadcastChannel("melodytrack-offline-access");
      channel.current = updates;
      updates.onmessage = (event: MessageEvent<unknown>) => {
        const data = event.data;
        if (
          typeof data === "object" &&
          data !== null &&
          "kind" in data &&
          data.kind === "logout" &&
          "staffId" in data &&
          data.staffId === owner.current
        )
          block();
      };
    } catch {
      /* Persistent marker and focus checks remain available. */
    }
    window.addEventListener("storage", changed);
    window.addEventListener("focus", check);
    window.addEventListener("online", check);
    document.addEventListener("visibilitychange", check);
    return () => {
      lock();
      updates?.close();
      channel.current = null;
      window.removeEventListener("storage", changed);
      window.removeEventListener("focus", check);
      window.removeEventListener("online", check);
      document.removeEventListener("visibilitychange", check);
    };
  }, [block, lock, revokeClientAccess]);

  useEffect(() => {
    if (state.status !== "unlocked") return;
    const prune = () => {
      void vault.current?.prune().catch(() => undefined);
    };
    const timer = setInterval(prune, 60 * 60 * 1000);
    window.addEventListener("focus", prune);
    return () => {
      clearInterval(timer);
      window.removeEventListener("focus", prune);
    };
  }, [state.status]);

  return (
    <Context.Provider
      value={{
        ...state,
        authenticate,
        clear,
        logout,
        unlock,
        get,
        put,
        remove,
        revokeClientAccess,
      }}
    >
      {children}
    </Context.Provider>
  );
}

export const useOfflineStorage = () => useContext(Context);
