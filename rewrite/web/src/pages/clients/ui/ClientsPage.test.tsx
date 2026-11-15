import {
  act,
  cleanup,
  fireEvent,
  render as renderUi,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { applicationApi } from "@/shared/api";
import { Notifications } from "@/shared/ui/notifications";
import { ClientsPage } from "./ClientsPage";

function render(ui: ReactNode) {
  return renderUi(ui, {
    wrapper: ({ children }) => (
      <>
        {children}
        <Notifications appearance="light" />
      </>
    ),
  });
}

const mocks = vi.hoisted(() => ({
  request: vi.fn(),
  openTraining: vi.fn(),
  permissions: [
    { permission: "clientsRead", allowed: true, dataScope: "available", timeScope: "any" },
  ],
}));
const dataChanges = vi.hoisted(() => ({ listeners: new Set<() => void>() }));
vi.mock("@/shared/api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/shared/api")>()),
  subscribeDataChanges: (listener: () => void) => {
    dataChanges.listeners.add(listener);
    return () => dataChanges.listeners.delete(listener);
  },
}));
vi.mock("@/entities/session", () => ({
  useStaffSession: () => ({
    request: mocks.request,
    staff: {
      staffId: "staff-a",
      permissions: mocks.permissions,
    },
  }),
}));
vi.mock("@/features/employee-training", () => ({
  useEmployeeTraining: () => ({ openClientTraining: mocks.openTraining }),
}));

const ids = {
  client: "10000000-0000-4000-8000-000000000001",
  source: "20000000-0000-4000-8000-000000000001",
  phone: "30000000-0000-4000-8000-000000000001",
  email: "30000000-0000-4000-8000-000000000002",
  link: "30000000-0000-4000-8000-000000000003",
  phoneKind: "40000000-0000-4000-8000-000000000001",
  emailKind: "40000000-0000-4000-8000-000000000002",
  linkKind: "40000000-0000-4000-8000-000000000003",
};

const client = {
  id: ids.client,
  firstName: "Анна",
  lastName: "Тестова",
  middleName: null,
  birthdate: "2001-05-06",
  sourceId: ids.source,
  sourceName: "Рекомендация",
  isArchived: false,
  createdAt: "2026-10-10T22:00:00.000Z",
  contacts: [
    {
      id: ids.phone,
      kindId: ids.phoneKind,
      kindName: "Телефон",
      format: "phone",
      value: "+7 (900) 123-45-67",
      comment: "Для связи",
      isPrimary: true,
    },
    {
      id: ids.email,
      kindId: ids.emailKind,
      kindName: "Электронная почта",
      format: "email",
      value: "anna@example.test",
      comment: null,
      isPrimary: false,
    },
    {
      id: ids.link,
      kindId: ids.linkKind,
      kindName: "Сайт",
      format: "link",
      value: "https://example.test/profile",
      comment: "Профиль",
      isPrimary: false,
    },
  ],
};

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function page(items = [client], page = 1, pageSize = 10, totalCount = 25) {
  return { items, page, pageSize, totalCount, totalPages: Math.ceil(totalCount / pageSize) };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((accept, fail) => {
    resolve = accept;
    reject = fail;
  });
  return { promise, resolve, reject };
}

beforeEach(() => {
  localStorage.clear();
  vi.clearAllMocks();
  dataChanges.listeners.clear();
  mocks.permissions = [
    { permission: "clientsRead", allowed: true, dataScope: "available", timeScope: "any" },
  ];
  mocks.request.mockImplementation((send, signal) => send(applicationApi(), signal));
  vi.stubGlobal(
    "matchMedia",
    vi.fn((media: string) => ({
      matches: false,
      media,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  );
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      return url.pathname.endsWith("/options")
        ? jsonResponse({ sources: [{ id: ids.source, name: "Рекомендация" }], contactKinds: [] })
        : jsonResponse(
            page(
              [client],
              Number(url.searchParams.get("page") ?? 1),
              Number(url.searchParams.get("pageSize") ?? 10),
            ),
          );
    }),
  );
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("client directory page", () => {
  it("shows card write actions only for effective available grants with a supported current time scope", async () => {
    const { rerender } = render(<ClientsPage />);
    await screen.findByText("Тестова Анна");
    expect(screen.queryByRole("button", { name: "Добавить клиента" })).toBeNull();

    mocks.permissions = [
      { permission: "clientsRead", allowed: true, dataScope: "available", timeScope: "any" },
      { permission: "clientsWrite", allowed: true, dataScope: "own", timeScope: "any" },
    ];
    rerender(<ClientsPage />);
    expect(screen.queryByRole("button", { name: "Добавить клиента" })).toBeNull();

    mocks.permissions = [
      { permission: "clientsRead", allowed: true, dataScope: "available", timeScope: "any" },
      {
        permission: "clientsWrite",
        allowed: true,
        dataScope: "available",
        timeScope: "currentMonth",
      },
    ];
    rerender(<ClientsPage />);
    expect(
      ((await screen.findByRole("button", { name: "Добавить клиента" })) as HTMLButtonElement)
        .disabled,
    ).toBe(false);
  });

  it("reuses create identifiers when the response is lost and the same draft is retried", async () => {
    mocks.permissions = [
      { permission: "clientsRead", allowed: true, dataScope: "available", timeScope: "any" },
      { permission: "clientsWrite", allowed: true, dataScope: "available", timeScope: "any" },
    ];
    const createBodies: Record<string, unknown>[] = [];
    let createAttempts = 0;
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/options"))
        return jsonResponse({
          sources: [{ id: ids.source, name: "Рекомендация" }],
          contactKinds: [],
        });
      if (url.pathname.replace(/\/+$/, "") === "/api/clients" && init?.method === "POST") {
        createBodies.push(
          JSON.parse(await new Response(init.body).text()) as Record<string, unknown>,
        );
        createAttempts += 1;
        if (createAttempts === 1) throw new TypeError("Failed to fetch");
        return jsonResponse(
          {
            client: { ...client, contacts: [] },
            hasDuplicateContacts: false,
          },
          201,
        );
      }
      return jsonResponse(page());
    });

    render(<ClientsPage />);
    await screen.findByText("Тестова Анна");
    fireEvent.click(screen.getByRole("button", { name: "Добавить клиента" }));
    fireEvent.change(screen.getByLabelText("Фамилия"), { target: { value: "Новая" } });
    fireEvent.change(screen.getByLabelText("Имя"), { target: { value: "Клиент" } });
    const sourceSelects = screen.getAllByRole("combobox", { name: "Источник" });
    const editorSource = sourceSelects[sourceSelects.length - 1];
    if (!editorSource) throw new Error("Expected the client editor source selector.");
    const sourceSurface = editorSource.closest(".ant-select-content");
    if (!sourceSurface) throw new Error("Expected the client editor source selector surface.");
    fireEvent.mouseDown(sourceSurface);
    await waitFor(() => expect(editorSource.getAttribute("aria-expanded")).toBe("true"));
    const sourcePopup = document.querySelector<HTMLElement>(
      ".ant-select-dropdown:not(.ant-select-dropdown-hidden)",
    );
    if (!sourcePopup) throw new Error("Expected the client editor source options to open.");
    fireEvent.click(
      within(sourcePopup).getByText("Рекомендация", {
        selector: ".ant-select-item-option-content",
      }),
    );
    expect(editorSource.closest(".client-editor-field")?.textContent).toContain("Рекомендация");
    fireEvent.click(screen.getByRole("button", { name: "Сохранить" }));
    await waitFor(() => expect(createBodies).toHaveLength(1));
    await screen.findByText("Нет соединения с сервером. Повторите попытку.");

    const saveButton = await screen.findByRole("button", { name: /Сохранить/ });
    await waitFor(() => expect(saveButton.classList.contains("ant-btn-loading")).toBe(false));
    expect((saveButton as HTMLButtonElement).disabled).toBe(false);
    fireEvent.click(saveButton);
    await waitFor(() => expect(createBodies).toHaveLength(2));
    expect(createBodies[1]).toEqual(createBodies[0]);
    expect(createBodies[0]?.commandId).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i,
    );
    expect(createBodies[0]?.clientId).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i,
    );
  });

  it("updates rows in the background while preserving the search input", async () => {
    const fetchMock = vi.mocked(fetch);
    let currentClient = client;
    fetchMock.mockImplementation(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      return url.pathname.endsWith("/options")
        ? jsonResponse({ sources: [{ id: ids.source, name: "Рекомендация" }], contactKinds: [] })
        : jsonResponse(page([currentClient]));
    });
    render(<ClientsPage />);
    await screen.findByText("Тестова Анна");
    const search = screen.getByRole("searchbox", { name: "ФИО или контакт" });
    fireEvent.change(search, { target: { value: "Тестова" } });
    await screen.findByText("Тестова Анна");
    await waitFor(() =>
      expect(
        screen.getByRole("region", { name: "Клиенты" }).querySelector("[aria-busy='false']"),
      ).not.toBeNull(),
    );

    currentClient = { ...client, firstName: "Обновлённая" };
    search.focus();
    expect(document.activeElement).toBe(search);
    await act(async () => {
      for (const listener of dataChanges.listeners) listener();
    });

    await screen.findByText("Тестова Обновлённая");
    expect(document.activeElement).toBe(search);
    expect((search as HTMLInputElement).value).toBe("Тестова");
    expect(
      screen.getByRole("region", { name: "Клиенты" }).querySelector("[aria-busy='true']"),
    ).toBeNull();
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("keeps an edited draft when an SSE refresh removes the client from the current result", async () => {
    mocks.permissions = [
      { permission: "clientsRead", allowed: true, dataScope: "available", timeScope: "any" },
      { permission: "clientsWrite", allowed: true, dataScope: "available", timeScope: "any" },
    ];
    let listedClients = [client];
    let detail = client;
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/options"))
        return jsonResponse({
          sources: [{ id: ids.source, name: "Рекомендация" }],
          contactKinds: [],
        });
      if (url.pathname === `/api/clients/${ids.client}`) return jsonResponse(detail);
      return jsonResponse(page(listedClients, 1, 10, listedClients.length));
    });

    render(<ClientsPage />);
    await screen.findByText("Тестова Анна");
    fireEvent.click(screen.getByRole("button", { name: "Изменить карточку: Тестова Анна" }));
    const firstName = await screen.findByLabelText("Имя");
    await waitFor(() => expect((firstName as HTMLInputElement).value).toBe("Анна"));
    fireEvent.change(firstName, { target: { value: "Черновик" } });
    listedClients = [];
    detail = { ...client, firstName: "Изменено на сервере" };

    await act(async () => {
      for (const listener of dataChanges.listeners) listener();
    });

    await waitFor(() =>
      expect((screen.getByLabelText("Имя") as HTMLInputElement).value).toBe("Черновик"),
    );
    expect(screen.getByText("Карточка изменилась на сервере. Ваш ввод сохранён.")).toBeTruthy();
  });

  it("refreshes the open client and clears it when the server denies access", async () => {
    const fetchMock = vi.mocked(fetch);
    let currentClient = client;
    let denyDirectory = false;
    fetchMock.mockImplementation(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/options")) return jsonResponse({ sources: [], contactKinds: [] });
      if (denyDirectory) return jsonResponse({}, 403);
      return jsonResponse(page([currentClient]));
    });
    render(<ClientsPage />);
    await screen.findByText("Тестова Анна");
    await waitFor(() =>
      expect(
        screen.getByRole("region", { name: "Клиенты" }).querySelector("[aria-busy='false']"),
      ).not.toBeNull(),
    );
    fireEvent.click(screen.getByRole("button", { name: "Тестова Анна" }));
    const drawer = await screen.findByRole("dialog");
    expect(within(drawer).getByText("Тестова Анна")).toBeTruthy();

    currentClient = { ...client, firstName: "Обновлённая" };
    await act(async () => {
      for (const listener of dataChanges.listeners) listener();
    });
    expect(await within(drawer).findByText("Тестова Обновлённая")).toBeTruthy();

    denyDirectory = true;
    await act(async () => {
      for (const listener of dataChanges.listeners) listener();
    });

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(screen.queryByText("Тестова Обновлённая")).toBeNull();
  });

  it("connects filters, multi-sort, paging, and client details to generated HTTP", async () => {
    const fetchMock = vi.mocked(fetch);
    render(<ClientsPage />);
    await screen.findByText("Тестова Анна");

    fireEvent.change(screen.getByRole("searchbox", { name: "ФИО или контакт" }), {
      target: { value: "anna@example.test" },
    });
    await waitFor(() => {
      expect(
        requestUrls(fetchMock).some((url) => url.searchParams.get("q") === "anna@example.test"),
      ).toBe(true);
    });
    await screen.findByRole("button", { name: "Сортировка: ФИО" });

    fireEvent.mouseDown(screen.getByRole("combobox", { name: "Статус" }));
    fireEvent.click(
      await screen.findByText("Архив", { selector: ".ant-select-item-option-content" }),
    );
    await screen.findByRole("button", { name: "Сортировка: ФИО" });
    fireEvent.mouseDown(screen.getByRole("combobox", { name: "Источник" }));
    fireEvent.click(
      await screen.findByText("Рекомендация", { selector: ".ant-select-item-option-content" }),
    );
    await screen.findByRole("button", { name: "Сортировка: ФИО" });
    fireEvent.click(screen.getByRole("button", { name: "Сортировка: ФИО" }));
    await screen.findByRole("button", { name: "Сортировка: ФИО" });
    fireEvent.click(screen.getByRole("button", { name: "Сортировка: Статус" }));
    await screen.findByRole("button", { name: "Сортировка: Статус" });

    await waitFor(() => {
      const urls = requestUrls(fetchMock);
      expect(
        urls.some(
          (url) =>
            url.searchParams.get("archive") === "archived" &&
            url.searchParams.get("sourceId") === ids.source &&
            url.searchParams.get("q") === "anna@example.test" &&
            url.searchParams.get("sort") === "name:desc,status:asc" &&
            url.searchParams.get("page") === "1" &&
            url.searchParams.get("pageSize") === "10",
        ),
      ).toBe(true);
    });

    const sizeChanger = screen.getAllByRole("combobox").at(-1);
    if (!sizeChanger) throw new Error("Missing page-size selector.");
    fireEvent.mouseDown(sizeChanger);
    fireEvent.click(
      await screen.findByText("20 / page", { selector: ".ant-select-item-option-content" }),
    );
    await waitFor(() => {
      expect(requestUrls(fetchMock).some((url) => url.searchParams.get("pageSize") === "20")).toBe(
        true,
      );
      expect(
        screen.getByRole("region", { name: "Клиенты" }).querySelector("[aria-busy='false']"),
      ).not.toBeNull();
    });
    fireEvent.click(within(screen.getByTitle("2")).getByRole("button"));
    await waitFor(() => {
      expect(
        requestUrls(fetchMock).some(
          (url) =>
            url.searchParams.get("page") === "2" && url.searchParams.get("pageSize") === "20",
        ),
      ).toBe(true);
    });

    fireEvent.click(screen.getByRole("button", { name: "Тестова Анна" }));
    const drawer = await screen.findByRole("dialog");
    expect(within(drawer).getByText("Тестова Анна")).toBeTruthy();
    expect(within(drawer).getByText("Для связи")).toBeTruthy();
    expect(within(drawer).getByText("anna@example.test").getAttribute("href")).toBe(
      "mailto:anna%40example.test",
    );
    expect(
      within(drawer)
        .getByRole("link", { name: /\+7 \(900\) 123-45-67/ })
        .getAttribute("href"),
    ).toBe("tel:+79001234567");
    expect(
      within(drawer)
        .getByRole("link", { name: "https://example.test/profile" })
        .getAttribute("href"),
    ).toBe("https://example.test/profile");
    expect(localStorage.length).toBe(0);
  });

  it("ignores an aborted search response that arrives after the newer result", async () => {
    const oldResponse = deferred<Response>();
    const newResponse = deferred<Response>();
    const pending: { query: string | null; signal: AbortSignal | null }[] = [];
    vi.stubGlobal(
      "fetch",
      vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
        const url = new URL(String(input));
        if (url.pathname.endsWith("/options"))
          return Promise.resolve(jsonResponse({ sources: [], contactKinds: [] }));
        const query = url.searchParams.get("q");
        if (query === "старый запрос" || query === "новый запрос") {
          pending.push({ query, signal: init?.signal ?? null });
          return query === "старый запрос" ? oldResponse.promise : newResponse.promise;
        }
        return Promise.resolve(jsonResponse(page()));
      }),
    );
    render(<ClientsPage />);
    await screen.findByText("Тестова Анна");
    const search = screen.getByRole("searchbox", { name: "ФИО или контакт" });

    fireEvent.change(search, { target: { value: "старый запрос" } });
    await waitFor(() => expect(pending.some((call) => call.query === "старый запрос")).toBe(true));
    fireEvent.change(search, { target: { value: "новый запрос" } });
    await waitFor(() => expect(pending.some((call) => call.query === "новый запрос")).toBe(true));

    const oldCall = pending.find((call) => call.query === "старый запрос");
    expect(oldCall?.signal?.aborted).toBe(true);
    const late = jsonResponse(
      page([{ ...client, id: "10000000-0000-4000-8000-000000000002", firstName: "Старый" }]),
    );
    await act(async () => {
      oldResponse.resolve(late);
      await waitFor(() => expect(late.bodyUsed).toBe(true));
    });
    expect(screen.queryByText("Тестова Старый")).toBeNull();
    expect(
      screen.getByRole("region", { name: "Клиенты" }).querySelector("[aria-busy='true']"),
    ).not.toBeNull();
    newResponse.resolve(
      jsonResponse(
        page([
          {
            ...client,
            id: "10000000-0000-4000-8000-000000000003",
            firstName: "Новый",
          },
        ]),
      ),
    );
    await screen.findByText("Тестова Новый");

    await waitFor(() => expect(screen.getByText("Тестова Новый")).toBeTruthy());
    expect(
      screen.getByRole("region", { name: "Клиенты" }).querySelector("[aria-busy='false']"),
    ).not.toBeNull();
    expect(screen.queryByText("Тестова Старый")).toBeNull();
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("keeps rows while a different search loads or fails and retries the request", async () => {
    const fetchMock = vi.mocked(fetch);
    render(<ClientsPage />);
    await screen.findByText("Тестова Анна");
    const failedResponse = deferred<Response>();
    let failedOnce = false;
    fetchMock.mockImplementation(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/options"))
        return jsonResponse({
          sources: [{ id: ids.source, name: "Рекомендация" }],
          contactKinds: [],
        });
      if (url.searchParams.get("q") === "ошибка" && !failedOnce) {
        failedOnce = true;
        return failedResponse.promise;
      }
      return jsonResponse(page());
    });

    fireEvent.change(screen.getByRole("searchbox", { name: "ФИО или контакт" }), {
      target: { value: "ошибка" },
    });
    await waitFor(() => expect(failedOnce).toBe(true));
    expect(screen.getByRole("button", { name: "Тестова Анна" })).toBeTruthy();
    expect(screen.getByRole("table", { name: "Клиенты" })).toBeTruthy();
    expect(
      screen.getByRole("region", { name: "Клиенты" }).querySelector("[aria-busy='true']"),
    ).not.toBeNull();
    failedResponse.resolve(jsonResponse({}, 503));
    await screen.findByText("Не удалось загрузить клиентов");
    expect(screen.getByRole("button", { name: "Тестова Анна" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Повторить" }));
    await waitFor(() => expect(fetchMock.mock.calls.length).toBeGreaterThan(3));
    await waitFor(() =>
      expect(
        screen.getByRole("region", { name: "Клиенты" }).querySelector("[aria-busy='false']"),
      ).not.toBeNull(),
    );
    expect(screen.getByRole("button", { name: "Тестова Анна" })).toBeTruthy();
  });

  it("retries failed source options without losing the client list", async () => {
    const optionRequests: string[] = [];
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockImplementation(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/options")) {
        optionRequests.push(url.pathname);
        return jsonResponse(
          optionRequests.length === 1
            ? {}
            : { sources: [{ id: ids.source, name: "Рекомендация" }], contactKinds: [] },
          optionRequests.length === 1 ? 503 : 200,
        );
      }
      return jsonResponse(page());
    });
    render(<ClientsPage />);

    await screen.findByText("Тестова Анна");
    await screen.findByText("Не удалось загрузить источники");
    const sourceSelect = screen.getByRole("combobox", { name: "Источник" });
    fireEvent.mouseDown(sourceSelect);
    expect(screen.queryByRole("listbox")).toBeNull();
    expect(
      screen.queryByText("Без источника", { selector: ".ant-select-item-option-content" }),
    ).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: "Повторить" }));
    await waitFor(() => expect(optionRequests).toHaveLength(2));
    fireEvent.mouseDown(screen.getByRole("combobox", { name: "Источник" }));
    fireEvent.click(
      await screen.findByText("Рекомендация", { selector: ".ant-select-item-option-content" }),
    );

    await waitFor(() => {
      expect(
        requestUrls(fetchMock).some((url) => url.searchParams.get("sourceId") === ids.source),
      ).toBe(true);
    });
    expect(screen.queryByText("Не удалось загрузить источники")).toBeNull();
    expect(screen.getByText("Тестова Анна")).toBeTruthy();
  });

  it("does not request the directory when the effective read grant is missing or unsupported", () => {
    mocks.permissions = [
      { permission: "clientsRead", allowed: false, dataScope: "available", timeScope: "any" },
    ];
    const denied = render(<ClientsPage />);
    expect(screen.getByText("Нет доступа к списку клиентов")).toBeTruthy();
    expect(fetch).not.toHaveBeenCalled();

    denied.unmount();
    mocks.permissions = [
      { permission: "clientsRead", allowed: true, dataScope: "own", timeScope: "any" },
    ];
    render(<ClientsPage />);
    expect(
      screen.getByText("Список своих клиентов станет доступен после подключения расписания"),
    ).toBeTruthy();
    expect(fetch).not.toHaveBeenCalled();
  });
});

function requestUrls(fetchMock: { mock: { calls: Parameters<typeof fetch>[] } }) {
  return fetchMock.mock.calls.map(([input]) => new URL(String(input)));
}
