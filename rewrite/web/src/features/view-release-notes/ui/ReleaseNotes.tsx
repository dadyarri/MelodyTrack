import { Button, Empty, Modal, Pagination, Skeleton } from "antd";
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
import { isAbortError, subscribeDataChanges } from "@/shared/api";
import {
  readSeenVersion,
  releaseNotesSeenKey,
  writeSeenVersion,
} from "../model/releaseNotesStorage";
import { loadUnseenReleases } from "../model/unseenReleases";
import "./release-notes.css";

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
  const [unseen, setUnseen] = useState<{
    scopeKey: string;
    data: { currentVersion: string; releases: ReleaseEntry[] };
  } | null>(null);
  const [manualOpen, setManualOpen] = useState(false);
  const [dismissed, setDismissed] = useState<string | null>(null);
  const [historyRequest, setHistoryRequest] = useState({ page: 1 });
  const [history, setHistory] = useState<LoadState<ReleaseHistory>>({ status: "loading" });
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
      void getCurrentRelease(request.signal)
        .then((release) => {
          if (!request.signal.aborted) {
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
          if (!request.signal.aborted && !isAbortError(error))
            setCurrent((previous) =>
              previous.status === "ready" ? previous : { status: "error" },
            );
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
        .catch(() => {
          // Keep the visible history when a background refresh fails.
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
  }, []);

  useEffect(() => {
    if (!canViewHistory || current.status !== "ready") return;
    const controller = new AbortController();
    const signal = AbortSignal.any([controller.signal, unseenReload.signal]);
    void loadUnseenReleases(seenVersion, (requestedPage) =>
      getReleaseHistory(requestedPage, signal),
    )
      .then((result) => {
        if (!signal.aborted) setUnseen({ scopeKey: unseenScopeKey, data: result });
      })
      .catch(() => {
        // History failure remains available through the manual modal and its retry button.
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
        if (!controller.signal.aborted && !isAbortError(error)) setHistory({ status: "error" });
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
      <Modal
        open={open}
        title="Что нового"
        className="release-notes-modal"
        onCancel={close}
        footer={
          <Button type="primary" onClick={close}>
            Понятно
          </Button>
        }
      >
        {open &&
          (automaticOpen ? (
            <ReleaseList releases={currentUnseen?.releases ?? []} />
          ) : history.status === "loading" ? (
            <Skeleton active paragraph={{ rows: 4 }} />
          ) : history.status === "error" ? (
            <div className="release-notes-empty">
              <Empty description="Не удалось загрузить список изменений" />
              <Button onClick={() => setHistoryRequest((value) => ({ ...value }))}>
                Повторить
              </Button>
            </div>
          ) : (
            <>
              <ReleaseList releases={history.data.releases} />
              {history.data.totalPages > 1 && (
                <Pagination
                  current={history.data.page}
                  pageSize={history.data.pageSize}
                  total={history.data.totalCount}
                  showSizeChanger={false}
                  onChange={(page) => setHistoryRequest({ page })}
                />
              )}
            </>
          ))}
      </Modal>
    </ReleaseNotesContext.Provider>
  );
}

export function ReleaseVersion() {
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
      className="release-version"
      disabled={!context.canOpen}
      onClick={context.openHistory}
      aria-label={version ? `Список изменений версии ${version}` : "Список изменений"}
    >
      {label}
    </button>
  ) : (
    <span className="release-version">{label}</span>
  );
}

const groups: Array<[keyof ReleaseChanges, string]> = [
  ["new", "Добавлено"],
  ["improved", "Улучшения"],
  ["fixed", "Исправления"],
  ["security", "Безопасность"],
];

function ReleaseList({ releases }: { releases: ReleaseEntry[] }) {
  if (!releases.length) return <Empty description="Опубликованных изменений пока нет" />;
  return (
    <div className="release-notes-list">
      {releases.map((release) => (
        <section key={release.version}>
          <h3>
            {release.parentVersion ? "Исправление " : ""}
            {release.version}
            {release.codename ? ` — ${release.codename}` : ""}
          </h3>
          {release.codenameMeaning && (
            <p className="release-codename-meaning">{release.codenameMeaning}</p>
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
                <div className="release-change-group" key={key}>
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
