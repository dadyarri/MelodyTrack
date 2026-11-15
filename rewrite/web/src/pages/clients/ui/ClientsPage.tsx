import { Alert, Button, Drawer, Select, Tag } from "antd";
import { useEffect, useId, useRef, useState } from "react";
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
import { useEmployeeTraining } from "@/features/employee-training";
import { isAbortError, subscribeDataChanges, toApiError } from "@/shared/api";
import {
  DataView as CollectionDataView,
  type DataViewColumn,
  type DataViewSort,
} from "@/shared/ui/data-view";
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

function ActionIcon({ kind }: { kind: "profile" | "phone" | "email" }) {
  return (
    <svg
      viewBox="0 0 24 24"
      width="18"
      height="18"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {kind === "profile" ? (
        <>
          <circle cx="12" cy="8" r="4" />
          <path d="M4 21v-2a8 8 0 0 1 16 0v2" />
        </>
      ) : kind === "phone" ? (
        <path d="m7 3-4 2c0 9 7 16 16 16l2-4-5-3-2 2a12 12 0 0 1-6-6l2-2Z" />
      ) : (
        <>
          <rect x="3" y="5" width="18" height="14" rx="2" />
          <path d="m3 6 9 7 9-7" />
        </>
      )}
    </svg>
  );
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

function QuickActions({ client, onOpen }: { client: DirectoryClient; onOpen: () => void }) {
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
        <ActionIcon kind="profile" />
      </button>
      {phoneHref && (
        <a href={phoneHref} aria-label={`Позвонить: ${clientName(client)}`} title="Позвонить">
          <ActionIcon kind="phone" />
        </a>
      )}
      {emailHref && (
        <a href={emailHref} aria-label={`Написать: ${clientName(client)}`} title="Написать письмо">
          <ActionIcon kind="email" />
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
  const queryRef = useRef(query);
  const dataRef = useRef(data);
  const latestDataRequest = useRef(0);
  const optionsRequestVersion = useRef(0);
  const backgroundRequest = useRef<AbortController | null>(null);
  const silentQueryRef = useRef<string | null>(null);
  queryRef.current = query;
  dataRef.current = data;
  const filterId = useId();
  const permission = staff?.permissions.find((entry) => entry.permission === "clientsRead");
  const allowed = permission?.allowed === true;
  const supportedScope = permission?.dataScope === "available" && permission.timeScope === "any";

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
              }
              if (!background) setError(apiError.message);
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
              setSelected((previous) =>
                previous
                  ? (result.items.find((client) => client.id === previous.id) ?? null)
                  : null,
              );
            }
          } else {
            const apiError = toApiError(directory.reason);
            if (apiError.status === 403) {
              setData(null);
              setSelected(null);
            }
            if (dataRef.current === null) setError(apiError.message);
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
          }
          if (dataRef.current === null) setError(apiError.message);
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

  function refresh() {
    reload.abort();
    setReload(new AbortController());
  }
  function change(patch: Partial<DirectoryQuery>) {
    setQuery((current) => ({ ...current, ...patch, page: 1 }));
  }
  const filterCount =
    Number(Boolean(query.q)) +
    Number(query.archive !== "active") +
    Number(Boolean(query.sourceId) || query.withoutSource);
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
      render: (client) => <QuickActions client={client} onOpen={() => setSelected(client)} />,
    },
  ];

  return (
    <div className="workspace-page directory-page">
      <div className="page-heading">
        <h1>Клиенты</h1>
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
          {optionsError && (
            <Alert
              type="warning"
              title="Не удалось загрузить источники"
              action={<Button onClick={refresh}>Повторить</Button>}
            />
          )}
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
            error={error}
            onRetry={refresh}
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
                  Работа
                  <Select
                    id={`${filterId}-archive`}
                    aria-label="Работа"
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
            title={selected ? clientName(selected) : "Клиент"}
            onClose={() => setSelected(null)}
            destroyOnHidden
            classNames={{ root: "directory-drawer" }}
          >
            {selected && (
              <div className="directory-profile">
                <dl className="directory-profile-fields">
                  <div>
                    <dt>Работа</dt>
                    <dd>{selected.isArchived ? "Архив" : "В работе"}</dd>
                  </div>
                  <div>
                    <dt>Источник</dt>
                    <dd>{selected.sourceName ?? "Не указан"}</dd>
                  </div>
                  <div>
                    <dt>Дата рождения</dt>
                    <dd>
                      {selected.birthdate
                        ? birthdayFormat.format(new Date(`${selected.birthdate}T00:00:00Z`))
                        : "—"}
                    </dd>
                  </div>
                  <div>
                    <dt>Добавлен</dt>
                    <dd>{dateFormat.format(new Date(selected.createdAt))}</dd>
                  </div>
                </dl>
                <section aria-label="Контакты">
                  <h3>Контакты</h3>
                  {selected.contacts.length ? (
                    selected.contacts.map((contact) => (
                      <Contact key={contact.id} contact={contact} />
                    ))
                  ) : (
                    <p className="directory-muted">Нет контактов</p>
                  )}
                </section>
              </div>
            )}
          </Drawer>
        </>
      )}
    </div>
  );
}
