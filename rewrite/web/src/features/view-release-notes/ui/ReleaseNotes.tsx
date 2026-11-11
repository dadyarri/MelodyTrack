import { Button, Empty, Modal, Pagination, Skeleton } from "antd";
import { createContext, type ReactNode, useCallback, useContext, useEffect, useRef, useState, useSyncExternalStore } from "react";
import { getCurrentRelease, getReleaseHistory, type CurrentRelease, type ReleaseChanges, type ReleaseEntry, type ReleaseHistory } from "@/entities/app-release";
import { isAbortError } from "@/shared/api";
import { readSeenVersion, releaseNotesSeenKey, writeSeenVersion } from "../model/releaseNotesStorage";
import { loadUnseenReleases } from "../model/unseenReleases";
import "./release-notes.css";

type LoadState<T> = { status: "loading" } | { status: "error" } | { status: "ready"; data: T };
type ReleaseNotesContextValue = { current: LoadState<CurrentRelease>; canViewHistory: boolean; canOpen: boolean; openHistory: () => void };
const ReleaseNotesContext = createContext<ReleaseNotesContextValue | null>(null);

export function ReleaseNotesProvider({ staffId, canViewHistory, enabled, children }: {
  staffId: string;
  canViewHistory: boolean;
  enabled: boolean;
  children: ReactNode;
}) {
  const [current, setCurrent] = useState<LoadState<CurrentRelease>>({ status: "loading" });
  const [currentRevision, setCurrentRevision] = useState(0);
  const [unseen, setUnseen] = useState<{ currentVersion: string; releases: ReleaseEntry[] } | null>(null);
  const [manualOpen, setManualOpen] = useState(false);
  const [dismissed, setDismissed] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const [retry, setRetry] = useState(0);
  const [history, setHistory] = useState<LoadState<ReleaseHistory>>({ status: "loading" });
  const manualAcknowledgement = useRef<string | null>(null);

  const subscribe = useCallback((onChange: () => void) => {
    const listener = (event: StorageEvent) => {
      if (event.key === releaseNotesSeenKey(staffId) || event.key === null) onChange();
    };
    window.addEventListener("storage", listener);
    return () => window.removeEventListener("storage", listener);
  }, [staffId]);
  const readSnapshot = useCallback(() => readSeenVersion(staffId), [staffId]);
  const seenVersion = useSyncExternalStore(subscribe, readSnapshot, () => null);
  const currentVersion = current.status === "ready" ? current.data.version : null;

  useEffect(() => {
    let controller: AbortController | null = null;
    let attemptedAt = 0;
    const load = (force = false) => {
      if (document.visibilityState !== "visible" || (!force && Date.now() - attemptedAt < 300_000)) return;
      attemptedAt = Date.now();
      controller?.abort();
      const request = new AbortController();
      controller = request;
      void getCurrentRelease(request.signal).then((release) => {
        if (!request.signal.aborted) {
          setCurrent({ status: "ready", data: release });
          setCurrentRevision((value) => value + 1);
        }
      }).catch((error: unknown) => {
        if (!request.signal.aborted && !isAbortError(error)) setCurrent((previous) => previous.status === "ready" ? previous : { status: "error" });
      });
    };
    const onFocus = () => load();
    const onOnline = () => load(true);
    load(true);
    window.addEventListener("focus", onFocus);
    window.addEventListener("online", onOnline);
    document.addEventListener("visibilitychange", onFocus);
    return () => {
      controller?.abort();
      window.removeEventListener("focus", onFocus);
      window.removeEventListener("online", onOnline);
      document.removeEventListener("visibilitychange", onFocus);
    };
  }, []);

  useEffect(() => {
    if (!canViewHistory || !currentVersion) return;
    const controller = new AbortController();
    setUnseen(null);
    void loadUnseenReleases(seenVersion, (requestedPage) => getReleaseHistory(requestedPage, controller.signal)).then((result) => {
      if (!controller.signal.aborted) setUnseen(result);
    }).catch(() => {
      // History failure remains available through the manual modal and its retry button.
    });
    return () => controller.abort();
  }, [canViewHistory, currentVersion, currentRevision, seenVersion]);

  useEffect(() => {
    if (!manualOpen || !canViewHistory) return;
    const controller = new AbortController();
    setHistory({ status: "loading" });
    void getReleaseHistory(page, controller.signal).then((result) => {
      if (controller.signal.aborted) return;
      if (page === 1) manualAcknowledgement.current = result.releases[0]?.version ?? null;
      setHistory({ status: "ready", data: result });
      setCurrent((previous) => previous.status === "ready" && previous.data.version === result.currentVersion
        ? previous : { status: "ready", data: { version: result.currentVersion, codename: null, codenameMeaning: null } });
    }).catch((error: unknown) => {
      if (!controller.signal.aborted && !isAbortError(error)) setHistory({ status: "error" });
    });
    return () => controller.abort();
  }, [manualOpen, canViewHistory, page, retry]);

  const automaticOpen = Boolean(enabled && canViewHistory && !manualOpen && unseen &&
    unseen.currentVersion === currentVersion && unseen.releases.length > 0 && dismissed !== currentVersion &&
    unseen.releases[0]?.version !== seenVersion);

  function close() {
    const acknowledged = manualOpen ? manualAcknowledgement.current : unseen?.releases[0]?.version;
    if (acknowledged) {
      writeSeenVersion(staffId, acknowledged);
      setDismissed(currentVersion);
    }
    setManualOpen(false);
  }

  function openHistory() {
    if (!canViewHistory || !enabled) return;
    manualAcknowledgement.current = null;
    setPage(1);
    setHistory({ status: "loading" });
    setManualOpen(true);
  }

  const open = enabled && canViewHistory && (manualOpen || automaticOpen);
  return (
    <ReleaseNotesContext.Provider value={{ current, canViewHistory, canOpen: enabled && canViewHistory, openHistory }}>
      {children}
      <Modal open={open} title="Что нового" className="release-notes-modal" onCancel={close} footer={<Button type="primary" onClick={close}>Понятно</Button>}>
        {open && (automaticOpen ? <ReleaseList releases={unseen?.releases ?? []} /> : history.status === "loading" ? (
          <Skeleton active paragraph={{ rows: 4 }} />
        ) : history.status === "error" ? (
          <div className="release-notes-empty"><Empty description="Не удалось загрузить список изменений" /><Button onClick={() => setRetry((value) => value + 1)}>Повторить</Button></div>
        ) : (
          <>
            <ReleaseList releases={history.data.releases} />
            {history.data.totalPages > 1 && <Pagination current={history.data.page} pageSize={history.data.pageSize} total={history.data.totalCount} showSizeChanger={false} onChange={setPage} />}
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
  const label = version ? `Версия ${version}` : context.current.status === "loading" ? "Версия…" : "Версия недоступна";
  return context.canViewHistory ? (
    <button type="button" className="release-version" disabled={!context.canOpen} onClick={context.openHistory} aria-label={version ? `Список изменений версии ${version}` : "Список изменений"}>{label}</button>
  ) : <span className="release-version">{label}</span>;
}

const groups: Array<[keyof ReleaseChanges, string]> = [["new", "Добавлено"], ["improved", "Улучшения"], ["fixed", "Исправления"], ["security", "Безопасность"]];

function ReleaseList({ releases }: { releases: ReleaseEntry[] }) {
  if (!releases.length) return <Empty description="Опубликованных изменений пока нет" />;
  return <div className="release-notes-list">{releases.map((release) => <section key={release.version}>
    <h3>{release.parentVersion ? "Исправление " : ""}{release.version}{release.codename ? ` — ${release.codename}` : ""}</h3>
    {release.codenameMeaning && <p className="release-codename-meaning">{release.codenameMeaning}</p>}
    {release.date && <time dateTime={release.date}>{new Intl.DateTimeFormat("ru-RU", { dateStyle: "long" }).format(new Date(`${release.date}T00:00:00`))}</time>}
    {groups.map(([key, label]) => release.changes[key].length > 0 && <div className="release-change-group" key={key}><h4>{label}</h4><ul>{release.changes[key].map((change) => <li key={change}>{change}</li>)}</ul></div>)}
  </section>)}</div>;
}
