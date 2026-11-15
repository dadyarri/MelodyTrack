import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { applicationApi } from "@/shared/api";
import { ClientsPage } from "./ClientsPage";

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
        ? jsonResponse({ sources: [{ id: ids.source, name: "Рекомендация" }] })
        : jsonResponse(page());
    }),
  );
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("client directory page", () => {
  it("updates rows in the background while preserving the search input", async () => {
    const fetchMock = vi.mocked(fetch);
    let currentClient = client;
    fetchMock.mockImplementation(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      return url.pathname.endsWith("/options")
        ? jsonResponse({ sources: [{ id: ids.source, name: "Рекомендация" }] })
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

  it("refreshes the open client and clears it when the server denies access", async () => {
    const fetchMock = vi.mocked(fetch);
    let currentClient = client;
    let denyDirectory = false;
    fetchMock.mockImplementation(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/options")) return jsonResponse({ sources: [] });
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

    fireEvent.mouseDown(screen.getByRole("combobox", { name: "Работа" }));
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
    await screen.findByRole("button", { name: "Сортировка: Статус" });
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
          return Promise.resolve(jsonResponse({ sources: [] }));
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

  it("clears old rows while loading a different search and retries a failed request", async () => {
    const fetchMock = vi.mocked(fetch);
    render(<ClientsPage />);
    await screen.findByText("Тестова Анна");
    let failedOnce = false;
    fetchMock.mockImplementation(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith("/options"))
        return jsonResponse({ sources: [{ id: ids.source, name: "Рекомендация" }] });
      if (url.searchParams.get("q") === "ошибка" && !failedOnce) {
        failedOnce = true;
        return jsonResponse({}, 503);
      }
      return jsonResponse(page());
    });

    fireEvent.change(screen.getByRole("searchbox", { name: "ФИО или контакт" }), {
      target: { value: "ошибка" },
    });
    await screen.findByRole("alert");
    expect(screen.queryByText("Тестова Анна")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Повторить" }));
    await waitFor(() => expect(fetchMock.mock.calls.length).toBeGreaterThan(3));
    await screen.findByText("Тестова Анна");
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
            : { sources: [{ id: ids.source, name: "Рекомендация" }] },
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
