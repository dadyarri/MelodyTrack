import {
  createContext,
  type ReactNode,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
  useSyncExternalStore,
} from "react";
import {
  type CurrentRelease,
  getCurrentRelease,
  getReleaseHistory,
  type ReleaseChanges,
  type ReleaseEntry,
  type ReleaseHistory,
} from "@/entities/app-release";
import { isAbortError, subscribeDataChanges, toApiError } from "@/shared/api";
import { Icon } from "@/shared/ui/Icon";
import { useErrorNotification } from "@/shared/ui/notifications";
import { PaperButton, PaperDialog, PaperPagination } from "@/shared/ui/paper";
import {
  readSeenVersion,
  releaseNotesSeenKey,
  writeSeenVersion,
} from "../model/releaseNotesStorage";
import { loadUnseenReleases } from "../model/unseenReleases";
import styles from "./release-notes.module.css";

type LoadState<T> = { status: "loading" } | { status: "error" } | { status: "ready"; data: T };
type ReleaseNotesContextValue = {
  current: LoadState<CurrentRelease>;
  canViewHistory: boolean;
  canOpen: boolean;
  openHistory: () => void;
};
const ReleaseNotesContext = createContext<ReleaseNotesContextValue | null>(null);

export function ReleaseNotesProvider({
  staffId,
  canViewHistory,
  enabled,
  children,
}: {
  staffId: string;
  canViewHistory: boolean;
  enabled: boolean;
  children: ReactNode;
}) {
  const [current, setCurrent] = useState<LoadState<CurrentRelease>>({ status: "loading" });
  const [currentError, setCurrentError] = useState<string | null>(null);
  const [unseen, setUnseen] = useState<{
    scopeKey: string;
    data: { currentVersion: string; releases: ReleaseEntry[] };
  } | null>(null);
  const [manualOpen, setManualOpen] = useState(false);
  const [dismissed, setDismissed] = useState<string | null>(null);
  const [historyRequest, setHistoryRequest] = useState({ page: 1 });
  const [history, setHistory] = useState<LoadState<ReleaseHistory>>({ status: "loading" });
  const [historyError, setHistoryError] = useState<string | null>(null);
  const [currentReload, setCurrentReload] = useState(() => new AbortController());
  const [unseenReload, setUnseenReload] = useState(() => new AbortController());
  const manualAcknowledgement = useRef<string | null>(null);
  const manualOpenRef = useRef(manualOpen);
  const historyRequestRef = useRef(historyRequest);
  const historyRequestVersion = useRef(0);
  manualOpenRef.current = manualOpen;
  historyRequestRef.current = historyRequest;

  const subscribe = useCallback(
    (onChange: () => void) => {
      const listener = (event: StorageEvent) => {
        if (event.key === releaseNotesSeenKey(staffId) || event.key === null) onChange();
      };
      window.addEventListener("storage", listener);
      return () => window.removeEventListener("storage", listener);
    },
    [staffId],
  );
  const readSnapshot = useCallback(() => readSeenVersion(staffId), [staffId]);
  const seenVersion = useSyncExternalStore(subscribe, readSnapshot, () => null);
  const currentVersion = current.status === "ready" ? current.data.version : null;
  const unseenScopeKey = JSON.stringify([staffId, canViewHistory, seenVersion]);
  const currentUnseen = unseen?.scopeKey === unseenScopeKey ? unseen.data : null;
  const retryCurrent = useCallback(() => {
    setCurrentError(null);
    setCurrentReload((previous) => {
      previous.abort();
      return new AbortController();
    });
  }, []);
  const retryHistory = useCallback(() => {
    if (manualOpenRef.current) {
      setHistoryRequest((value) => ({ ...value }));
      return;
    }
    setUnseenReload((previous) => {
      previous.abort();
      return new AbortController();
    });
  }, []);

  useErrorNotification(currentError, {
    title: "Не удалось проверить текущую версию",
    retry: retryCurrent,
  });
  useErrorNotification(historyError, {
    title: "Не удалось загрузить список изменений",
    retry: retryHistory,
  });

  useEffect(() => {
    let controller: AbortController | null = null;
    let backgroundHistoryController: AbortController | null = null;
    let attemptedAt = 0;
    const load = (force = false) => {
      if (document.visibilityState !== "visible" || (!force && Date.now() - attemptedAt < 300_000))
        return;
      attemptedAt = Date.now();
      controller?.abort();
      const request = new AbortController();
      controller = request;
      void getCurrentRelease(AbortSignal.any([request.signal, currentReload.signal]))
        .then((release) => {
          if (!request.signal.aborted) {
            setCurrentError(null);
            setCurrent((previous) =>
              previous.status === "ready" &&
              previous.data.version === release.version &&
              previous.data.codename === release.codename &&
              previous.data.codenameMeaning === release.codenameMeaning
                ? previous
                : { status: "ready", data: release },
            );
          }
        })
        .catch((error: unknown) => {
          if (!request.signal.aborted && !isAbortError(error)) {
            const message = toApiError(error).message;
            setCurrentError(message);
            setCurrent((previous) =>
              previous.status === "ready" ? previous : { status: "error" },
            );
          }
        });
    };
    const onFocus = () => load();
    const onOnline = () => {
      load(true);
      setUnseenReload((previous) => {
        previous.abort();
        return new AbortController();
      });
    };
    const unsubscribe = subscribeDataChanges(() => {
      load(true);
      setUnseenReload((previous) => {
        previous.abort();
        return new AbortController();
      });
      if (!manualOpenRef.current) return;
      const version = ++historyRequestVersion.current;
      const page = historyRequestRef.current.page;
      backgroundHistoryController?.abort();
      const request = new AbortController();
      backgroundHistoryController = request;
      void getReleaseHistory(page, request.signal)
        .then((result) => {
          if (request.signal.aborted || version !== historyRequestVersion.current) return;
          if (page === 1) manualAcknowledgement.current = result.releases[0]?.version ?? null;
          setHistoryError(null);
          setHistory({ status: "ready", data: result });
          setCurrent((previous) =>
            previous.status === "ready" && previous.data.version === result.currentVersion
              ? previous
              : {
                  status: "ready",
                  data: { version: result.currentVersion, codename: null, codenameMeaning: null },
                },
          );
        })
        .catch((error: unknown) => {
          if (!request.signal.aborted && !isAbortError(error))
            setHistoryError(toApiError(error).message);
        });
    });
    load(true);
    window.addEventListener("focus", onFocus);
    window.addEventListener("online", onOnline);
    document.addEventListener("visibilitychange", onFocus);
    return () => {
      controller?.abort();
      backgroundHistoryController?.abort();
      window.removeEventListener("focus", onFocus);
      window.removeEventListener("online", onOnline);
      document.removeEventListener("visibilitychange", onFocus);
      unsubscribe();
    };
  }, [currentReload]);

  useEffect(() => {
    if (!canViewHistory || current.status !== "ready") return;
    const controller = new AbortController();
    const signal = AbortSignal.any([controller.signal, unseenReload.signal]);
    void loadUnseenReleases(seenVersion, (requestedPage) =>
      getReleaseHistory(requestedPage, signal),
    )
      .then((result) => {
        if (!signal.aborted) {
          setUnseen({ scopeKey: unseenScopeKey, data: result });
          setHistoryError(null);
        }
      })
      .catch((error: unknown) => {
        if (!signal.aborted && !isAbortError(error)) setHistoryError(toApiError(error).message);
      });
    return () => controller.abort();
  }, [canViewHistory, current, seenVersion, unseenReload, unseenScopeKey]);

  useEffect(() => {
    if (!manualOpen || !canViewHistory) return;
    const controller = new AbortController();
    const version = ++historyRequestVersion.current;
    setHistory({ status: "loading" });
    void getReleaseHistory(historyRequest.page, controller.signal)
      .then((result) => {
        if (controller.signal.aborted || version !== historyRequestVersion.current) return;
        if (historyRequest.page === 1)
          manualAcknowledgement.current = result.releases[0]?.version ?? null;
        setHistoryError(null);
        setHistory({ status: "ready", data: result });
        setCurrent((previous) =>
          previous.status === "ready" && previous.data.version === result.currentVersion
            ? previous
            : {
                status: "ready",
                data: { version: result.currentVersion, codename: null, codenameMeaning: null },
              },
        );
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted && !isAbortError(error)) {
          const message = toApiError(error).message;
          setHistoryError(message);
          setHistory({ status: "error" });
        }
      });
    return () => controller.abort();
  }, [manualOpen, canViewHistory, historyRequest]);

  const automaticOpen = Boolean(
    enabled &&
      canViewHistory &&
      !manualOpen &&
      currentUnseen &&
      currentUnseen.releases.length > 0 &&
      dismissed !== currentUnseen.currentVersion &&
      currentUnseen.releases[0]?.version !== seenVersion,
  );

  function close() {
    const acknowledged = manualOpen
      ? manualAcknowledgement.current
      : currentUnseen?.releases[0]?.version;
    if (acknowledged) {
      writeSeenVersion(staffId, acknowledged);
      setDismissed(manualOpen ? currentVersion : (currentUnseen?.currentVersion ?? currentVersion));
    }
    setManualOpen(false);
  }

  function openHistory() {
    if (!canViewHistory || !enabled) return;
    manualAcknowledgement.current = null;
    setHistoryError(null);
    setHistoryRequest({ page: 1 });
    setHistory({ status: "loading" });
    setManualOpen(true);
  }

  const open = enabled && canViewHistory && (manualOpen || automaticOpen);
  return (
    <ReleaseNotesContext.Provider
      value={{ current, canViewHistory, canOpen: enabled && canViewHistory, openHistory }}
    >
      {children}
      <PaperDialog
        open={open}
        title="Что нового"
        className={styles.dialog}
        onClose={close}
        footer={
          <PaperButton variant="primary" onClick={close}>
            Понятно
          </PaperButton>
        }
      >
        {open &&
          (automaticOpen ? (
            <ReleaseList releases={currentUnseen?.releases ?? []} />
          ) : history.status === "loading" ? (
            <div className={styles.loading} role="status">
              Загрузка списка изменений…
            </div>
          ) : history.status === "error" ? (
            <div className={styles.empty} role="status">
              Список изменений пока недоступен.
            </div>
          ) : (
            <>
              <ReleaseList releases={history.data.releases} />
              {history.data.totalPages > 1 && (
                <PaperPagination
                  page={history.data.page}
                  pageSize={history.data.pageSize}
                  total={history.data.totalCount}
                  showPageSize={false}
                  ariaLabel="Страницы списка изменений"
                  onChange={(page) => setHistoryRequest({ page })}
                />
              )}
            </>
          ))}
      </PaperDialog>
    </ReleaseNotesContext.Provider>
  );
}

export function ReleaseVersion({
  onOpen,
  compact = false,
}: {
  onOpen?: () => void;
  compact?: boolean;
} = {}) {
  const context = useContext(ReleaseNotesContext);
  if (!context) throw new Error("ReleaseNotesProvider is required.");
  const version = context.current.status === "ready" ? context.current.data.version : null;
  const label = version
    ? `Версия ${version}`
    : context.current.status === "loading"
      ? "Версия…"
      : "Версия недоступна";
  return context.canViewHistory ? (
    <button
      type="button"
      className={styles.version}
      title={compact ? label : undefined}
      disabled={!context.canOpen}
      onClick={() => {
        onOpen?.();
        context.openHistory();
      }}
      aria-label={version ? `Список изменений версии ${version}` : "Список изменений"}
    >
      {compact ? <Icon name="history" /> : label}
    </button>
  ) : compact ? (
    <span className={styles.version} title={label} role="img" aria-label={label}>
      <Icon name="history" />
    </span>
  ) : (
    <span className={styles.version}>{label}</span>
  );
}

const groups: Array<[keyof ReleaseChanges, string]> = [
  ["new", "Добавлено"],
  ["improved", "Улучшения"],
  ["fixed", "Исправления"],
  ["security", "Безопасность"],
];

function ReleaseList({ releases }: { releases: ReleaseEntry[] }) {
  if (!releases.length)
    return <div className={styles.empty}>Опубликованных изменений пока нет</div>;
  return (
    <div className={styles.list}>
      {releases.map((release) => (
        <section key={release.version}>
          <h3>
            {release.parentVersion ? "Исправление " : ""}
            {release.version}
            {release.codename ? ` — ${release.codename}` : ""}
          </h3>
          {release.codenameMeaning && (
            <p className={styles.codenameMeaning}>{release.codenameMeaning}</p>
          )}
          {release.date && (
            <time dateTime={release.date}>
              {new Intl.DateTimeFormat("ru-RU", { dateStyle: "long" }).format(
                new Date(`${release.date}T00:00:00`),
              )}
            </time>
          )}
          {groups.map(
            ([key, label]) =>
              release.changes[key].length > 0 && (
                <div className={styles.changeGroup} key={key}>
                  <h4>{label}</h4>
                  <ul>
                    {release.changes[key].map((change) => (
                      <li key={change}>{change}</li>
                    ))}
                  </ul>
                </div>
              ),
          )}
        </section>
      ))}
    </div>
  );
}
