import { fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CollectionDataView, type DataViewColumn, type DataViewProps } from "./DataView";

type Row = { id: string; name: string; source: string };
const columns: DataViewColumn<Row>[] = [
  { key: "name", title: "Имя", render: (row) => row.name, sortable: true },
  { key: "source", title: "Источник", render: (row) => row.source, sortable: true },
];
const rows: Row[] = [
  { id: "b", name: "Бета", source: "Сайт" },
  { id: "a", name: "Альфа", source: "Рекомендация" },
];

function props(overrides: Partial<DataViewProps<Row>> = {}): DataViewProps<Row> {
  return {
    ariaLabel: "Учебный список",
    columns,
    items: rows,
    rowKey: (row) => row.id,
    total: 65,
    page: 1,
    pageSize: 20,
    onPageChange: vi.fn(),
    search: "",
    onSearchChange: vi.fn(),
    sort: [],
    onSortChange: vi.fn(),
    ...overrides,
  };
}

beforeEach(() => {
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
});
afterEach(() => vi.unstubAllGlobals());

describe("data view", () => {
  it("adds a secondary sort without removing the primary priority", () => {
    const initial = props({ sort: [{ key: "name", direction: "asc" }] });
    const view = render(<CollectionDataView {...initial} />);

    fireEvent.click(screen.getByRole("button", { name: "Сортировка: Источник" }));

    expect(initial.onSortChange).toHaveBeenLastCalledWith([
      { key: "name", direction: "asc" },
      { key: "source", direction: "asc" },
    ]);
    view.rerender(
      <CollectionDataView
        {...initial}
        sort={[
          { key: "name", direction: "asc" },
          { key: "source", direction: "asc" },
        ]}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "Сортировка: Источник" }));
    expect(initial.onSortChange).toHaveBeenLastCalledWith([
      { key: "name", direction: "asc" },
      { key: "source", direction: "desc" },
    ]);
  });

  it("opens a clicked row but preserves independent actions inside its cells", () => {
    const onRowClick = vi.fn();
    const onAction = vi.fn();
    const nameColumn: DataViewColumn<Row> = {
      key: "name",
      title: "Имя",
      render: (row) => row.name,
    };
    render(
      <CollectionDataView
        {...props({ onRowClick })}
        columns={[
          nameColumn,
          {
            key: "action",
            title: "Действие",
            render: () => (
              <button type="button" onClick={onAction}>
                Открыть действие
              </button>
            ),
          },
        ]}
      />,
    );

    fireEvent.click(screen.getByText("Бета"));
    expect(onRowClick).toHaveBeenCalledWith(rows[0]);
    const action = screen.getAllByRole("button", { name: "Открыть действие" })[0];
    if (!action) throw new Error("Missing action button");
    fireEvent.click(action);
    expect(onAction).toHaveBeenCalledOnce();
    expect(onRowClick).toHaveBeenCalledOnce();
  });

  it("requests sorting while preserving the order received from the server", () => {
    const initial = props();
    const view = render(<CollectionDataView {...initial} />);

    fireEvent.click(screen.getByRole("button", { name: "Сортировка: Имя" }));

    expect(initial.onSortChange).toHaveBeenCalledWith([{ key: "name", direction: "asc" }]);
    const tableRows = within(screen.getByRole("table")).getAllByRole("row");
    expect(tableRows[1]?.textContent).toContain("Бета");
    expect(tableRows[2]?.textContent).toContain("Альфа");

    view.rerender(<CollectionDataView {...initial} sort={[{ key: "name", direction: "desc" }]} />);
    expect(screen.getByRole("columnheader", { name: /Имя/ }).getAttribute("aria-sort")).toBe(
      "descending",
    );
    fireEvent.click(screen.getByRole("button", { name: "Сортировка: Имя" }));
    expect(initial.onSortChange).toHaveBeenLastCalledWith([]);
  });

  it("preserves a stable empty state after a failed read and keeps received rows visible", () => {
    const initial = props({ items: [], total: 0, loading: true });
    const view = render(<CollectionDataView {...initial} />);
    expect(screen.getByText("Загрузка…")).toBeTruthy();
    expect(screen.queryByText("Ничего не найдено")).toBeNull();

    view.rerender(<CollectionDataView {...initial} loading={false} />);
    expect(screen.getByText("Ничего не найдено")).toBeTruthy();

    view.rerender(
      <CollectionDataView {...initial} loading={false} error="Список клиентов пока недоступен." />,
    );
    expect(screen.getByRole("status").textContent).toContain("Список клиентов пока недоступен.");
    expect(screen.queryByRole("alert")).toBeNull();

    view.rerender(<CollectionDataView {...props({ loading: false, error: "Сбой обновления" })} />);
    expect(screen.getByRole("table").textContent).toContain("Бета");
    expect(screen.getByRole("table").textContent).toContain("Альфа");
  });

  it("reports search and pagination changes without filtering the received page", () => {
    const initial = props();
    render(<CollectionDataView {...initial} />);

    fireEvent.change(screen.getByRole("searchbox"), { target: { value: "совпадение" } });
    fireEvent.click(screen.getByRole("button", { name: "Страница 2" }));

    expect(initial.onSearchChange).toHaveBeenCalledWith("совпадение");
    expect(initial.onPageChange).toHaveBeenCalledWith(2, 20);
    expect(within(screen.getByRole("table")).getAllByRole("row")).toHaveLength(3);
  });
});
