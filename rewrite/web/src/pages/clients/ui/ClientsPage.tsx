import { Alert, Button, Drawer, Select, Tag, Tooltip } from "antd";
import { useCallback, useEffect, useId, useRef, useState } from "react";
import {
  clientName,
  type DirectoryClient,
  type DirectoryContact,
  type DirectoryOptions,
  type DirectoryPage,
  type DirectoryQuery,
  getClientDirectory,
  getClientDirectoryOptions,
  initialDirectoryQuery,
} from "@/entities/client-directory";
import { useStaffSession } from "@/entities/session";
import {
  type ClientCardDraft,
  ClientEditorDrawer,
  ClientNotesSection,
  createClientCard,
  getClientCard,
  getClientNotes,
  restoreClientCard,
  updateClientCard,
  updateClientNotes,
} from "@/features/edit-client";
import { useEmployeeTraining } from "@/features/employee-training";
import { isAbortError, subscribeDataChanges, toApiError } from "@/shared/api";
import { uuidv7 } from "@/shared/lib/uuidv7";
import {
  DataView as CollectionDataView,
  type DataViewColumn,
  type DataViewSort,
} from "@/shared/ui/data-view";
import { Icon } from "@/shared/ui/Icon";
import { useErrorNotification } from "@/shared/ui/notifications";
import "./clients.css";

const dateFormat = new Intl.DateTimeFormat("ru-RU", {
  timeZone: "Europe/Moscow",
  dateStyle: "medium",
  timeStyle: "short",
});
const birthdayFormat = new Intl.DateTimeFormat("ru-RU", { timeZone: "UTC", dateStyle: "medium" });

function contactHref(contact: DirectoryContact): string | undefined {
  if (contact.format === "phone") {
    const number = contact.value.replace(/[^+\d]/g, "");
    return /\d/.test(number) ? `tel:${number}` : undefined;
  }
  if (contact.format === "email") return `mailto:${encodeURIComponent(contact.value)}`;
  try {
    const url = new URL(contact.value);
    if (url.protocol === "https:" || url.protocol === "http:") return url.href;
  } catch {
    // A historical contact without a usable URL remains readable.
  }
  return undefined;
}

function Contact({ contact }: { contact: DirectoryContact }) {
  const href = contactHref(contact);
  return (
    <div className="directory-contact">
      <span className="directory-contact-kind">
        {contact.kindName}
        {contact.isPrimary ? " · основной" : ""}
      </span>
      {href ? (
        <a
          href={href}
          {...(contact.format === "link" ? { target: "_blank", rel: "noopener noreferrer" } : {})}
        >
          {contact.value}
        </a>
      ) : (
        <span>{contact.value}</span>
      )}
      {contact.comment && <span className="directory-contact-comment">{contact.comment}</span>}
    </div>
  );
}

function QuickActions({
  client,
  canWrite,
  onOpen,
  onEdit,
}: {
  client: DirectoryClient;
  canWrite: boolean;
  onOpen: () => void;
  onEdit: () => void;
}) {
  const phone =
    client.contacts.find((contact) => contact.format === "phone" && contact.isPrimary) ??
    client.contacts.find((contact) => contact.format === "phone");
  const email =
    client.contacts.find((contact) => contact.format === "email" && contact.isPrimary) ??
    client.contacts.find((contact) => contact.format === "email");
  const phoneHref = phone && contactHref(phone);
  const emailHref = email && contactHref(email);
  return (
    <div className="directory-quick-actions">
      <button
        type="button"
        aria-label={`Открыть карточку: ${clientName(client)}`}
        title="Открыть карточку"
        onClick={onOpen}
      >
        <Icon name="profile" size={18} />
      </button>
      {canWrite ? (
        <button
          type="button"
          aria-label={`Изменить карточку: ${clientName(client)}`}
          title="Изменить карточку"
          onClick={onEdit}
        >
          <Icon name="edit" size={18} />
        </button>
      ) : null}
      {phoneHref && (
        <a href={phoneHref} aria-label={`Позвонить: ${clientName(client)}`} title="Позвонить">
          <Icon name="phone" size={18} />
        </a>
      )}
      {emailHref && (
        <a href={emailHref} aria-label={`Написать: ${clientName(client)}`} title="Написать письмо">
          <Icon name="email" size={18} />
        </a>
      )}
    </div>
  );
}

function UnknownValue({ label }: { label: string }) {
  return (
    <span className="directory-unknown" role="img" aria-label={label} title={label}>
      —
    </span>
  );
}

function directorySort(value: string): DataViewSort[] {
  return value.split(",").flatMap((part) => {
    const [key, direction] = part.split(":");
    return (key === "name" || key === "status") && (direction === "asc" || direction === "desc")
      ? [{ key, direction }]
      : [];
  });
}

function sameCardState(left: DirectoryClient, right: DirectoryClient): boolean {
  return (
    JSON.stringify({
      firstName: left.firstName,
      lastName: left.lastName,
      middleName: left.middleName,
      birthdate: left.birthdate,
      sourceId: left.sourceId,
      contacts: left.contacts.map(({ id, kindId, value, comment, isPrimary }) => ({
        id,
        kindId,
        value,
        comment,
        isPrimary,
      })),
    }) ===
    JSON.stringify({
      firstName: right.firstName,
      lastName: right.lastName,
      middleName: right.middleName,
      birthdate: right.birthdate,
      sourceId: right.sourceId,
      contacts: right.contacts.map(({ id, kindId, value, comment, isPrimary }) => ({
        id,
        kindId,
        value,
        comment,
        isPrimary,
      })),
    })
  );
}

function permissionAllowed(
  staff: ReturnType<typeof useStaffSession>["staff"],
  name: string,
  dataScope?: "available" | "own",
  timeScopes?: readonly ("any" | "today" | "currentMonth")[],
): boolean {
  const grant = staff?.permissions.find((entry) => entry.permission === name);
  return Boolean(
    grant?.allowed === true &&
      (dataScope === undefined || grant.dataScope === dataScope) &&
      (timeScopes === undefined ||
        timeScopes.includes(grant.timeScope as "any" | "today" | "currentMonth")),
  );
}

export function ClientsPage() {
  const { staff } = useStaffSession();
  return staff ? <ClientDirectory key={staff.staffId} /> : null;
}

function ClientDirectory() {
  const { request, staff } = useStaffSession();
  const { openClientTraining } = useEmployeeTraining();
  const [query, setQuery] = useState<DirectoryQuery>(initialDirectoryQuery);
  const [data, setData] = useState<DirectoryPage | null>(null);
  const [options, setOptions] = useState<DirectoryOptions | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [optionsError, setOptionsError] = useState(false);
  const [reload, setReload] = useState(() => new AbortController());
  const [selected, setSelected] = useState<DirectoryClient | null>(null);
  const [clientCard, setClientCard] = useState<DirectoryClient | null>(null);
  const [clientCardLoading, setClientCardLoading] = useState(false);
  const [clientCardError, setClientCardError] = useState<string | null>(null);
  const [cardReloadKey, setCardReloadKey] = useState(0);
  const [editorMode, setEditorMode] = useState<"create" | "edit" | null>(null);
  const [editorSaving, setEditorSaving] = useState(false);
  const [editorError, setEditorError] = useState<string | null>(null);
  const [editorStale, setEditorStale] = useState(false);
  const [editorResetKey, setEditorResetKey] = useState(0);
  const [duplicateWarning, setDuplicateWarning] = useState(false);
  const [notes, setNotes] = useState<string | null>(null);
  const [noteHistory, setNoteHistory] = useState<
    Awaited<ReturnType<typeof getClientNotes>>["history"]
  >([]);
  const [notesForClient, setNotesForClient] = useState<string | null>(null);
  const [notesLoading, setNotesLoading] = useState(false);
  const [notesSaving, setNotesSaving] = useState(false);
  const [notesError, setNotesError] = useState<string | null>(null);
  const [notesResetKey, setNotesResetKey] = useState(0);
  const [restoreBusy, setRestoreBusy] = useState(false);
  const queryRef = useRef(query);
  const dataRef = useRef(data);
  const selectedIdRef = useRef(selected?.id ?? null);
  const cardReloadKeyRef = useRef(cardReloadKey);
  const notesSaveClientId = useRef<string | null>(null);
  const editorReloadClientId = useRef<string | null>(null);
  const restoreClientId = useRef<string | null>(null);
  const editorModeRef = useRef(editorMode);
  const latestDataRequest = useRef(0);
  const optionsRequestVersion = useRef(0);
  const backgroundRequest = useRef<AbortController | null>(null);
  const silentQueryRef = useRef<string | null>(null);
  const editorBase = useRef<DirectoryClient | null>(null);
  const editorRequest = useRef<AbortController | null>(null);
  const editorReloadRequest = useRef<AbortController | null>(null);
  const editorCommand = useRef<{ signature: string; commandId: string; clientId: string } | null>(
    null,
  );
  const notesCommand = useRef<{ signature: string; commandId: string } | null>(null);
  const restoreCommand = useRef<{ clientId: string; commandId: string } | null>(null);
  const notesSaveRequest = useRef<AbortController | null>(null);
  const notesSaveVersion = useRef(0);
  const canReadNotesRef = useRef(false);
  const restoreRequest = useRef<AbortController | null>(null);
  queryRef.current = query;
  dataRef.current = data;
  selectedIdRef.current = selected?.id ?? null;
  cardReloadKeyRef.current = cardReloadKey;
  editorModeRef.current = editorMode;
  const filterId = useId();
  const retryDirectory = useCallback(() => {
    setReload((current) => {
      current.abort();
      return new AbortController();
    });
  }, []);
  const retryOptions = useCallback(() => {
    setOptionsError(false);
    retryDirectory();
  }, [retryDirectory]);
  const retryCard = useCallback(() => setCardReloadKey((key) => key + 1), []);
  useErrorNotification(error, { title: "Не удалось загрузить клиентов", retry: retryDirectory });
  useErrorNotification(optionsError ? "Не удалось загрузить справочники." : null, {
    title: "Не удалось загрузить источники",
    retry: retryOptions,
  });
  useErrorNotification(clientCardError, {
    title: "Не удалось загрузить карточку клиента",
    retry: retryCard,
  });
  const permission = staff?.permissions.find((entry) => entry.permission === "clientsRead");
  const allowed = permission?.allowed === true;
  const supportedScope = permission?.dataScope === "available" && permission.timeScope === "any";
  const canWrite =
    allowed &&
    supportedScope &&
    permissionAllowed(staff, "clientsWrite", "available", ["any", "today", "currentMonth"]);
  const canReadNotes =
    allowed && supportedScope && permissionAllowed(staff, "clientNotesRead", "available", ["any"]);
  const canWriteNotes =
    allowed &&
    supportedScope &&
    permissionAllowed(staff, "clientNotesWrite", "available", ["any", "today", "currentMonth"]);
  canReadNotesRef.current = canReadNotes;

  useEffect(() => {
    if (allowed && supportedScope && canWrite) return;
    editorRequest.current?.abort();
    editorRequest.current = null;
    editorReloadRequest.current?.abort();
    editorReloadRequest.current = null;
    setClientCardLoading(false);
    restoreRequest.current?.abort();
    restoreRequest.current = null;
    editorBase.current = null;
    editorCommand.current = null;
    setEditorMode(null);
    setEditorSaving(false);
    setRestoreBusy(false);
    setEditorError(null);
    setEditorStale(false);
  }, [allowed, supportedScope, canWrite]);

  useEffect(() => {
    if (canReadNotes) return;
    notesSaveRequest.current?.abort();
    notesSaveRequest.current = null;
    notesSaveVersion.current += 1;
    setNotesSaving(false);
  }, [canReadNotes]);

  useEffect(() => {
    if (!allowed || !supportedScope) return;
    const controller = new AbortController();
    const signal = AbortSignal.any([controller.signal, reload.signal]);
    const requestVersion = ++optionsRequestVersion.current;
    setOptionsError(false);
    void request(getClientDirectoryOptions, signal)
      .then((result) => {
        if (!signal.aborted && requestVersion === optionsRequestVersion.current) setOptions(result);
      })
      .catch((failure: unknown) => {
        if (
          !signal.aborted &&
          requestVersion === optionsRequestVersion.current &&
          !isAbortError(failure)
        )
          setOptionsError(true);
      });
    return () => controller.abort();
  }, [allowed, supportedScope, request, reload]);

  useEffect(() => {
    const clientId = selected?.id;
    const requestReloadKey = cardReloadKey;
    if (!clientId || !allowed || !supportedScope) {
      setClientCard(null);
      setClientCardError(null);
      setClientCardLoading(false);
      setNotes(null);
      setNoteHistory([]);
      setNotesForClient(null);
      return;
    }
    const controller = new AbortController();
    const signal = AbortSignal.any([controller.signal, reload.signal]);
    setClientCardLoading(true);
    setClientCardError(null);
    void request((api, requestSignal) => getClientCard(api, requestSignal, clientId), signal)
      .then((result) => {
        if (!signal.aborted && requestReloadKey === cardReloadKeyRef.current) {
          setClientCard(result);
          if (editorMode === "edit" && editorBase.current?.id !== result.id) {
            editorBase.current = result;
            setEditorResetKey((key) => key + 1);
          }
        }
      })
      .catch((failure: unknown) => {
        if (
          signal.aborted ||
          requestReloadKey !== cardReloadKeyRef.current ||
          isAbortError(failure)
        )
          return;
        const apiError = toApiError(failure);
        if (apiError.status === 403) {
          setClientCard(null);
          setSelected(null);
          setNotes(null);
          setNoteHistory([]);
          setNotesForClient(null);
          editorRequest.current?.abort();
          editorReloadRequest.current?.abort();
          editorBase.current = null;
          editorCommand.current = null;
          setEditorMode(null);
          setEditorSaving(false);
        }
        setClientCardError(apiError.message);
      })
      .finally(() => {
        if (!signal.aborted && requestReloadKey === cardReloadKeyRef.current)
          setClientCardLoading(false);
      });
    return () => controller.abort();
  }, [selected?.id, allowed, supportedScope, request, reload, cardReloadKey, editorMode]);

  useEffect(() => {
    const clientId = selected?.id;
    const requestReloadKey = cardReloadKey;
    if (!clientId || !canReadNotes) {
      setNotes(null);
      setNoteHistory([]);
      setNotesForClient(null);
      setNotesError(null);
      setNotesLoading(false);
      return;
    }
    const controller = new AbortController();
    const signal = AbortSignal.any([controller.signal, reload.signal]);
    setNotesLoading(true);
    setNotesError(null);
    void request((api, requestSignal) => getClientNotes(api, requestSignal, clientId), signal)
      .then((result) => {
        if (signal.aborted || requestReloadKey !== cardReloadKeyRef.current) return;
        setNotes(result.notes);
        setNoteHistory(result.history);
        setNotesForClient(clientId);
      })
      .catch((failure: unknown) => {
        if (
          signal.aborted ||
          requestReloadKey !== cardReloadKeyRef.current ||
          isAbortError(failure)
        )
          return;
        const apiError = toApiError(failure);
        if (apiError.status === 403) {
          setNotes(null);
          setNoteHistory([]);
          setNotesForClient(null);
          notesSaveRequest.current?.abort();
          notesSaveRequest.current = null;
          notesSaveVersion.current += 1;
          setNotesSaving(false);
        }
        setNotesError(apiError.message);
      })
      .finally(() => {
        if (!signal.aborted && requestReloadKey === cardReloadKeyRef.current)
          setNotesLoading(false);
      });
    return () => controller.abort();
  }, [selected?.id, canReadNotes, request, reload, cardReloadKey]);

  useEffect(() => {
    const selectedClientId = selected?.id ?? null;
    if (notesSaveClientId.current !== selectedClientId) {
      notesSaveRequest.current?.abort();
      notesSaveRequest.current = null;
      notesSaveClientId.current = null;
      notesSaveVersion.current += 1;
      setNotesSaving(false);
    }
    return () => {
      notesSaveRequest.current?.abort();
      notesSaveVersion.current += 1;
    };
  }, [selected?.id]);

  useEffect(() => {
    const selectedClientId = selected?.id ?? null;
    if (editorReloadClientId.current !== selectedClientId) {
      editorReloadRequest.current?.abort();
      editorReloadRequest.current = null;
      editorReloadClientId.current = null;
      setClientCardLoading(false);
    }
    if (restoreClientId.current !== selectedClientId) {
      restoreRequest.current?.abort();
      restoreRequest.current = null;
      restoreClientId.current = null;
      setRestoreBusy(false);
    }
  }, [selected?.id]);

  useEffect(() => {
    if (canReadNotes) return;
    setNotes(null);
    setNoteHistory([]);
    setNotesForClient(null);
    setNotesError(null);
  }, [canReadNotes]);

  useEffect(() => {
    const background = silentQueryRef.current === JSON.stringify(query) && dataRef.current !== null;
    if (background) silentQueryRef.current = null;
    else {
      setData(null);
      setError(null);
    }
    if (!allowed || !supportedScope) {
      setOptions(null);
      setSelected(null);
      setLoading(false);
      return;
    }
    const controller = new AbortController();
    const signal = AbortSignal.any([controller.signal, reload.signal]);
    backgroundRequest.current?.abort();
    if (!background) setLoading(true);
    const timer = window.setTimeout(
      () => {
        const requestVersion = ++latestDataRequest.current;
        void request((api, requestSignal) => getClientDirectory(api, requestSignal, query), signal)
          .then((result) => {
            if (signal.aborted || requestVersion !== latestDataRequest.current) return;
            if (
              (result.totalPages > 0 && query.page > result.totalPages) ||
              (result.totalPages === 0 && query.page !== 1)
            ) {
              const page = result.totalPages > 0 ? result.totalPages : 1;
              if (background) silentQueryRef.current = JSON.stringify({ ...query, page });
              setQuery((previous) => ({ ...previous, page }));
              return;
            }
            setData(result);
            setError(null);
          })
          .catch((failure: unknown) => {
            if (
              !signal.aborted &&
              requestVersion === latestDataRequest.current &&
              !isAbortError(failure)
            ) {
              const apiError = toApiError(failure);
              if (apiError.status === 403) {
                setData(null);
                setOptions(null);
                setSelected(null);
                editorRequest.current?.abort();
                editorReloadRequest.current?.abort();
                editorBase.current = null;
                editorCommand.current = null;
                setEditorMode(null);
                setEditorSaving(false);
              }
              setError(apiError.message);
            }
          })
          .finally(() => {
            if (!signal.aborted && requestVersion === latestDataRequest.current) setLoading(false);
          });
      },
      background ? 0 : 250,
    );
    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [allowed, supportedScope, query, reload, request]);

  useEffect(() => {
    if (!allowed || !supportedScope) return;
    let active = true;
    let refreshPending = false;
    const refresh = () => {
      if (!active) return;
      if (backgroundRequest.current) {
        refreshPending = true;
        return;
      }
      const requestQuery = queryRef.current;
      const controller = new AbortController();
      backgroundRequest.current = controller;
      const requestVersion = ++latestDataRequest.current;
      const optionsVersion = ++optionsRequestVersion.current;
      void Promise.allSettled([
        request((api, signal) => getClientDirectory(api, signal, requestQuery), controller.signal),
        request(getClientDirectoryOptions, controller.signal),
      ])
        .then(([directory, sources]) => {
          if (controller.signal.aborted || requestVersion !== latestDataRequest.current) return;
          if (directory.status === "fulfilled") {
            const result = directory.value;
            if (
              (result.totalPages > 0 && requestQuery.page > result.totalPages) ||
              (result.totalPages === 0 && requestQuery.page !== 1)
            ) {
              const page = result.totalPages > 0 ? result.totalPages : 1;
              silentQueryRef.current = JSON.stringify({ ...requestQuery, page });
              setQuery((previous) => ({ ...previous, page }));
            } else {
              setData(result);
              setLoading(false);
              setError(null);
              if (selectedIdRef.current) setCardReloadKey((key) => key + 1);
              setSelected((previous) =>
                previous
                  ? (result.items.find((client) => client.id === previous.id) ??
                    (editorModeRef.current === "edit" ? previous : null))
                  : null,
              );
            }
          } else {
            const apiError = toApiError(directory.reason);
            if (apiError.status === 403) {
              setData(null);
              setSelected(null);
            }
            setError(apiError.message);
            setLoading(false);
          }
          if (sources.status === "fulfilled" && optionsVersion === optionsRequestVersion.current) {
            setOptions(sources.value);
            setOptionsError(false);
          } else if (
            sources.status === "rejected" &&
            optionsVersion === optionsRequestVersion.current &&
            toApiError(sources.reason).status === 403
          ) {
            setOptions(null);
          }
          // Transient background failures keep the last successful list and sources visible.
        })
        .catch((failure: unknown) => {
          if (controller.signal.aborted || requestVersion !== latestDataRequest.current) return;
          const apiError = toApiError(failure);
          if (apiError.status === 403) {
            setData(null);
            setOptions(null);
            setSelected(null);
            editorRequest.current?.abort();
            editorReloadRequest.current?.abort();
            editorBase.current = null;
            editorCommand.current = null;
            setEditorMode(null);
            setEditorSaving(false);
          }
          setError(apiError.message);
          setLoading(false);
        })
        .finally(() => {
          if (backgroundRequest.current === controller) backgroundRequest.current = null;
          if (active && refreshPending) {
            refreshPending = false;
            refresh();
          }
        });
    };
    const unsubscribe = subscribeDataChanges(refresh);
    return () => {
      active = false;
      unsubscribe();
      backgroundRequest.current?.abort();
    };
  }, [allowed, supportedScope, request]);

  const refresh = retryDirectory;
  function change(patch: Partial<DirectoryQuery>) {
    setQuery((current) => ({ ...current, ...patch, page: 1 }));
  }
  const filterCount =
    Number(Boolean(query.q)) +
    Number(query.archive !== "active") +
    Number(Boolean(query.sourceId) || query.withoutSource);

  function beginCreate() {
    editorCommand.current = null;
    editorReloadRequest.current?.abort();
    editorReloadRequest.current = null;
    setClientCardLoading(false);
    editorBase.current = null;
    setEditorError(null);
    setEditorStale(false);
    setDuplicateWarning(false);
    setEditorMode("create");
  }

  function beginEdit(client: DirectoryClient) {
    editorCommand.current = null;
    editorReloadRequest.current?.abort();
    editorReloadRequest.current = null;
    setClientCardLoading(false);
    editorBase.current = clientCard?.id === client.id ? clientCard : null;
    setSelected(client);
    setEditorError(null);
    setEditorStale(false);
    setDuplicateWarning(false);
    setEditorMode("edit");
  }

  function closeEditor() {
    if (editorSaving) return;
    editorRequest.current?.abort();
    editorReloadRequest.current?.abort();
    editorReloadRequest.current = null;
    setClientCardLoading(false);
    restoreRequest.current?.abort();
    restoreRequest.current = null;
    editorCommand.current = null;
    editorBase.current = null;
    setEditorMode(null);
    setEditorError(null);
    setEditorStale(false);
  }

  async function saveEditor(draft: ClientCardDraft) {
    const mode = editorMode;
    const original = editorBase.current;
    if (!mode || (mode === "edit" && !original)) return;
    const controller = new AbortController();
    editorRequest.current?.abort();
    editorRequest.current = controller;
    const signature = JSON.stringify({ mode, clientId: original?.id ?? null, original, draft });
    if (editorCommand.current?.signature !== signature) {
      editorCommand.current = { signature, commandId: uuidv7(), clientId: uuidv7() };
    }
    const command = editorCommand.current;
    setEditorSaving(true);
    setEditorError(null);
    try {
      const result = await request(async (api, signal) => {
        if (mode === "create") return createClientCard(api, signal, draft, command);
        if (!original) throw new Error("Missing client snapshot for update.");
        const hasDuplicateContacts = await updateClientCard(
          api,
          signal,
          original,
          draft,
          command.commandId,
        );
        return { client: null, hasDuplicateContacts };
      }, controller.signal);
      if (controller.signal.aborted) return;
      setDuplicateWarning(result.hasDuplicateContacts);
      setEditorMode(null);
      editorBase.current = null;
      editorCommand.current = null;
      if (result.client) {
        setSelected(result.client);
        setClientCard(result.client);
      }
      refresh();
    } catch (failure) {
      if (controller.signal.aborted || isAbortError(failure)) return;
      const apiError = toApiError(failure);
      setEditorStale(apiError.status === 409);
      setEditorError(
        apiError.status === 409
          ? "Карточка изменилась после открытия формы. Загрузите актуальные данные, затем повторите ввод."
          : apiError.message,
      );
    } finally {
      if (editorRequest.current === controller) editorRequest.current = null;
      setEditorSaving(false);
    }
  }

  async function reloadEditor() {
    const clientId = editorBase.current?.id ?? selected?.id;
    if (!clientId) return;
    const controller = new AbortController();
    editorReloadRequest.current?.abort();
    editorReloadRequest.current = controller;
    editorReloadClientId.current = clientId;
    setClientCardLoading(true);
    setEditorError(null);
    try {
      const current = await request(
        (api, signal) => getClientCard(api, signal, clientId),
        controller.signal,
      );
      if (
        controller.signal.aborted ||
        editorModeRef.current !== "edit" ||
        selectedIdRef.current !== clientId
      )
        return;
      editorBase.current = current;
      setClientCard(current);
      setEditorStale(false);
      setEditorResetKey((key) => key + 1);
    } catch (failure) {
      if (
        !controller.signal.aborted &&
        !isAbortError(failure) &&
        selectedIdRef.current === clientId
      )
        setEditorError(toApiError(failure).message);
    } finally {
      if (editorReloadRequest.current === controller) {
        editorReloadRequest.current = null;
        editorReloadClientId.current = null;
        setClientCardLoading(false);
      }
    }
  }

  async function saveNotes(value: string | null, expected: string | null) {
    if (!selected || !canWriteNotes) return;
    const clientId = selected.id;
    const controller = new AbortController();
    notesSaveRequest.current?.abort();
    notesSaveRequest.current = controller;
    const saveVersion = ++notesSaveVersion.current;
    const signature = JSON.stringify({ clientId, value, expected });
    if (notesCommand.current?.signature !== signature) {
      notesCommand.current = { signature, commandId: uuidv7() };
    }
    const commandId = notesCommand.current.commandId;
    notesSaveClientId.current = clientId;
    setNotesSaving(true);
    setNotesError(null);
    try {
      await request(
        (api, signal) => updateClientNotes(api, signal, clientId, value, expected, commandId),
        controller.signal,
      );
      if (
        controller.signal.aborted ||
        saveVersion !== notesSaveVersion.current ||
        selectedIdRef.current !== clientId ||
        !canReadNotesRef.current
      )
        return;
      notesCommand.current = null;
      setNotes(value);
      setNotesForClient(clientId);
      setNotesResetKey((key) => key + 1);
      setCardReloadKey((key) => key + 1);
    } catch (failure) {
      if (
        isAbortError(failure) ||
        saveVersion !== notesSaveVersion.current ||
        selectedIdRef.current !== clientId ||
        !canReadNotesRef.current
      )
        return;
      const apiError = toApiError(failure);
      setNotesError(
        apiError.status === 409
          ? "Заметки изменились на сервере. Загрузите актуальную версию и перенесите изменения вручную."
          : apiError.message,
      );
      if (apiError.status === 409) setCardReloadKey((key) => key + 1);
    } finally {
      if (saveVersion === notesSaveVersion.current) {
        if (notesSaveRequest.current === controller) {
          notesSaveRequest.current = null;
          notesSaveClientId.current = null;
        }
        setNotesSaving(false);
      }
    }
  }

  function reloadNotes() {
    setNotesResetKey((key) => key + 1);
    setCardReloadKey((key) => key + 1);
    setNotesError(null);
  }

  async function restoreSelected() {
    if (!selected?.isArchived || !canWrite) return;
    const clientId = selected.id;
    const controller = new AbortController();
    restoreRequest.current?.abort();
    restoreRequest.current = controller;
    restoreClientId.current = clientId;
    if (restoreCommand.current?.clientId !== clientId) {
      restoreCommand.current = { clientId, commandId: uuidv7() };
    }
    const commandId = restoreCommand.current.commandId;
    setRestoreBusy(true);
    setClientCardError(null);
    try {
      await request(
        (api, signal) => restoreClientCard(api, signal, clientId, commandId),
        controller.signal,
      );
      if (controller.signal.aborted || selectedIdRef.current !== clientId) return;
      restoreCommand.current = null;
      refresh();
      setCardReloadKey((key) => key + 1);
    } catch (failure) {
      if (
        !controller.signal.aborted &&
        !isAbortError(failure) &&
        selectedIdRef.current === clientId
      )
        setClientCardError(toApiError(failure).message);
    } finally {
      if (restoreRequest.current === controller) {
        restoreRequest.current = null;
        restoreClientId.current = null;
        setRestoreBusy(false);
      }
    }
  }

  const columns: DataViewColumn<DirectoryClient>[] = [
    {
      key: "name",
      title: "ФИО",
      sortable: true,
      render: (client) => (
        <button type="button" className="directory-name" onClick={() => setSelected(client)}>
          {clientName(client)}
        </button>
      ),
    },
    {
      key: "status",
      title: "Статус",
      sortable: true,
      render: (client) => (client.isArchived ? <Tag>Архив</Tag> : null),
    },
    {
      key: "lastAttended",
      title: "Последняя запись",
      render: () => <UnknownValue label="Нет данных о последнем посещении" />,
    },
    {
      key: "nextAppointment",
      title: "Следующая запись",
      render: () => <UnknownValue label="Нет данных о ближайшей записи" />,
    },
    {
      key: "balance",
      title: "Баланс",
      render: () => <UnknownValue label="Нет данных о балансе" />,
    },
    {
      key: "actions",
      title: "Действия",
      render: (client) => (
        <QuickActions
          client={client}
          canWrite={canWrite}
          onOpen={() => setSelected(client)}
          onEdit={() => beginEdit(client)}
        />
      ),
    },
  ];
  const selectedCard = selected ? (clientCard?.id === selected.id ? clientCard : selected) : null;
  const editorClient = editorMode === "edit" ? (selectedCard ?? editorBase.current) : null;
  const serverCardChanged = Boolean(
    editorMode === "edit" &&
      editorBase.current &&
      clientCard?.id === editorBase.current.id &&
      !sameCardState(editorBase.current, clientCard),
  );

  return (
    <div className="workspace-page directory-page">
      <div className="page-heading">
        <h1>Клиенты</h1>
        {canWrite ? (
          <Tooltip
            title={
              !options?.sources.length
                ? "Для создания сначала добавьте источник в справочник."
                : undefined
            }
          >
            <Button
              type="primary"
              icon={<Icon name="plus" />}
              onClick={beginCreate}
              disabled={!options?.sources.length}
            >
              Добавить клиента
            </Button>
          </Tooltip>
        ) : null}
      </div>
      {!allowed ? (
        <Alert type="warning" title="Нет доступа к списку клиентов" />
      ) : !supportedScope ? (
        <Alert
          type="info"
          title={
            permission?.dataScope === "own"
              ? "Список своих клиентов станет доступен после подключения расписания"
              : "Список недоступен с текущим ограничением доступа по времени"
          }
        />
      ) : (
        <>
          <CollectionDataView
            ariaLabel="Клиенты"
            columns={columns}
            items={data?.items ?? []}
            rowKey={(client) => client.id}
            onRowClick={setSelected}
            total={data?.totalCount ?? 0}
            page={query.page}
            pageSize={query.pageSize}
            onPageChange={(page, pageSize) =>
              setQuery((current) => ({
                ...current,
                page: pageSize === current.pageSize ? page : 1,
                pageSize,
              }))
            }
            search={query.q}
            searchPlaceholder="ФИО или контакт"
            onSearchChange={(q) => change({ q })}
            sort={directorySort(query.sort)}
            onSortChange={(sort) =>
              change({ sort: sort.map((entry) => `${entry.key}:${entry.direction}`).join(",") })
            }
            loading={loading}
            error={error ? "Список клиентов пока недоступен." : null}
            activeFilterCount={filterCount}
            onResetFilters={() =>
              setQuery({ ...initialDirectoryQuery, pageSize: query.pageSize, sort: query.sort })
            }
            actions={
              <>
                <Button onClick={openClientTraining}>Помощь</Button>
                <Button onClick={refresh} disabled={loading}>
                  Обновить
                </Button>
              </>
            }
            emptyDescription={filterCount ? "По этим условиям клиентов нет" : "Клиентов пока нет"}
            filters={
              <div className="directory-filters">
                <label htmlFor={`${filterId}-archive`}>
                  Статус
                  <Select
                    id={`${filterId}-archive`}
                    aria-label="Статус"
                    value={query.archive}
                    onChange={(archive: DirectoryQuery["archive"]) => change({ archive })}
                    options={[
                      { value: "active", label: "В работе" },
                      { value: "archived", label: "Архив" },
                      { value: "all", label: "Все" },
                    ]}
                  />
                </label>
                <label htmlFor={`${filterId}-source`}>
                  Источник
                  <Select
                    id={`${filterId}-source`}
                    aria-label="Источник"
                    value={query.withoutSource ? "missing" : query.sourceId || "all"}
                    disabled={!options}
                    onChange={(source: string) =>
                      change({
                        sourceId: source === "all" || source === "missing" ? "" : source,
                        withoutSource: source === "missing",
                      })
                    }
                    options={[
                      { value: "all", label: "Все источники" },
                      { value: "missing", label: "Без источника" },
                      ...(options?.sources.map((source) => ({
                        value: source.id,
                        label: source.name,
                      })) ?? []),
                    ]}
                  />
                </label>
              </div>
            }
          />
          <Drawer
            open={selected !== null}
            placement="right"
            size="min(480px, 100vw)"
            title={selectedCard ? clientName(selectedCard) : "Клиент"}
            onClose={() => setSelected(null)}
            destroyOnHidden
            classNames={{ root: "directory-drawer" }}
          >
            {selectedCard && (
              <div className="directory-profile">
                <div className="client-profile-actions">
                  {canWrite ? (
                    <Tooltip title="Изменить карточку">
                      <Button
                        aria-label="Изменить карточку"
                        icon={<Icon name="edit" />}
                        onClick={() => beginEdit(selectedCard)}
                      />
                    </Tooltip>
                  ) : null}
                  {selectedCard.isArchived && canWrite ? (
                    <Tooltip title="Вернуть в работу">
                      <Button
                        type="primary"
                        aria-label="Вернуть в работу"
                        icon={<Icon name="restore" />}
                        loading={restoreBusy}
                        onClick={restoreSelected}
                      />
                    </Tooltip>
                  ) : null}
                  <Tooltip title="Проверка связанных записей ещё не подключена.">
                    <Button
                      disabled
                      aria-label="В архив"
                      icon={<Icon name="archive" />}
                      aria-describedby="client-archive-reason"
                    />
                  </Tooltip>
                </div>
                {selectedCard.isArchived ? <Tag>Архив</Tag> : null}
                {clientCardLoading ? <p role="status">Обновляем карточку…</p> : null}
                {duplicateWarning ? (
                  <Alert
                    type="warning"
                    showIcon
                    title="Есть контакт, совпадающий с контактом другого клиента. Сохранение разрешено."
                  />
                ) : null}
                <Alert
                  id="client-archive-reason"
                  type="info"
                  showIcon
                  title="Архивирование пока недоступно"
                  description="Проверка связанных записей ещё не подключена."
                />
                <dl className="directory-profile-fields">
                  <div>
                    <dt>Статус</dt>
                    <dd>{selectedCard.isArchived ? "Архив" : "В работе"}</dd>
                  </div>
                  <div>
                    <dt>Источник</dt>
                    <dd>{selectedCard.sourceName ?? "Не указан"}</dd>
                  </div>
                  <div>
                    <dt>Дата рождения</dt>
                    <dd>
                      {selectedCard.birthdate
                        ? birthdayFormat.format(new Date(`${selectedCard.birthdate}T00:00:00Z`))
                        : "—"}
                    </dd>
                  </div>
                  <div>
                    <dt>Добавлен</dt>
                    <dd>{dateFormat.format(new Date(selectedCard.createdAt))}</dd>
                  </div>
                </dl>
                <section aria-label="Контакты">
                  <h3>Контакты</h3>
                  {selectedCard.contacts.length ? (
                    selectedCard.contacts.map((contact) => (
                      <Contact key={contact.id} contact={contact} />
                    ))
                  ) : (
                    <p className="directory-muted">Нет контактов</p>
                  )}
                </section>
                {canReadNotes && notesForClient === selectedCard.id ? (
                  <ClientNotesSection
                    key={`${selectedCard.id}-${notesResetKey}`}
                    notes={notes}
                    history={noteHistory}
                    canEdit={canWriteNotes}
                    loading={notesLoading}
                    saving={notesSaving}
                    error={notesError}
                    resetKey={notesResetKey}
                    onSave={saveNotes}
                    onReload={reloadNotes}
                  />
                ) : canReadNotes ? (
                  <p role="status">Загружаем заметки…</p>
                ) : null}
              </div>
            )}
          </Drawer>
          <ClientEditorDrawer
            open={editorMode !== null}
            client={editorClient}
            sourceOptions={options?.sources ?? []}
            contactKinds={options?.contactKinds ?? []}
            loading={editorMode === "edit" && clientCardLoading}
            saving={editorSaving}
            error={editorError}
            stale={editorStale || serverCardChanged}
            hasDuplicateContacts={duplicateWarning}
            resetKey={editorResetKey}
            onClose={closeEditor}
            onSave={saveEditor}
            onReload={reloadEditor}
          />
        </>
      )}
    </div>
  );
}
