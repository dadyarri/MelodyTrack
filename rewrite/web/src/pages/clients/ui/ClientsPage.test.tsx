import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ClientsPage } from "./ClientsPage";

const mocks = vi.hoisted(() => ({
  request: vi.fn(),
  list: vi.fn(),
  options: vi.fn(),
  openTraining: vi.fn(),
}));
vi.mock("@/entities/session", () => ({
  useStaffSession: () => ({
    request: mocks.request,
    staff: {
      staffId: "staff-a",
      permissions: [
        { permission: "clientsRead", allowed: true, dataScope: "available", timeScope: "any" },
      ],
    },
  }),
}));
vi.mock("@/features/employee-training", () => ({
  useEmployeeTraining: () => ({ openClientTraining: mocks.openTraining }),
}));
vi.mock("@/entities/client-directory", async (original) => ({
  ...(await original<typeof import("@/entities/client-directory")>()),
  getClientDirectory: mocks.list,
  getClientDirectoryOptions: mocks.options,
}));
vi.mock("@/shared/ui/data-view", async (original) => ({
  ...(await original<typeof import("@/shared/ui/data-view")>()),
  DataView: ({
    items,
    search,
    onSearchChange,
    page,
    onPageChange,
    error,
  }: {
    items: { id: string; firstName: string }[];
    search: string;
    onSearchChange: (value: string) => void;
    page: number;
    onPageChange: (page: number, size: number) => void;
    error: string | null;
  }) => (
    <>
      <input
        aria-label="Поиск"
        value={search}
        onChange={(event) => onSearchChange(event.target.value)}
      />
      <button type="button" onClick={() => onPageChange(page + 1, 10)}>
        Следующая
      </button>
      {error && <div role="alert">{error}</div>}
      {items.map((item) => (
        <div key={item.id}>{item.firstName}</div>
      ))}
    </>
  ),
}));

beforeEach(() => {
  localStorage.clear();
  vi.clearAllMocks();
  mocks.request.mockImplementation((send, signal) => send({}, signal));
  mocks.options.mockResolvedValue({ sources: [], contactKinds: [] });
  mocks.list.mockResolvedValue({
    items: [{ id: "client-a", firstName: "Анна" }],
    page: 1,
    pageSize: 20,
    totalCount: 40,
    totalPages: 2,
  });
});
afterEach(cleanup);

describe("client directory page", () => {
  it("resets pagination when search changes without saving contact text in browser storage", async () => {
    render(<ClientsPage />);
    await screen.findByText("Анна");
    fireEvent.click(screen.getByRole("button", { name: "Следующая" }));
    await waitFor(() => expect(mocks.list.mock.lastCall?.[2].page).toBe(2));

    fireEvent.change(screen.getByRole("textbox", { name: "Поиск" }), {
      target: { value: "+7 000 000-00-00" },
    });

    await waitFor(() =>
      expect(mocks.list.mock.lastCall?.[2]).toMatchObject({ page: 1, q: "+7 000 000-00-00" }),
    );
    expect(localStorage.length).toBe(0);
  });

  it("clears previous client rows while a different search is loading and when it fails", async () => {
    render(<ClientsPage />);
    await screen.findByText("Анна");
    mocks.list.mockRejectedValue(new Error("network"));

    fireEvent.change(screen.getByRole("textbox", { name: "Поиск" }), {
      target: { value: "другой" },
    });

    await waitFor(() => expect(screen.queryByText("Анна")).toBeNull());
    await screen.findByRole("alert");
    expect(screen.queryByText("Анна")).toBeNull();
  });
});
