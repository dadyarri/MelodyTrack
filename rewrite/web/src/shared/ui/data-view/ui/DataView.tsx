import { Button, Input, Pagination, Spin } from "antd";
import { type ReactNode, useEffect, useRef } from "react";
import "./data-view.css";

export type DataViewSort = { key: string; direction: "asc" | "desc" };
export type DataViewColumn<T> = {
  key: string;
  title: string;
  render: (item: T) => ReactNode;
  sortable?: boolean;
};
export type DataViewProps<T> = {
  ariaLabel: string;
  columns: readonly DataViewColumn<T>[];
  items: readonly T[];
  rowKey: (item: T) => string;
  total: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number, pageSize: number) => void;
  search: string;
  onSearchChange: (value: string) => void;
  searchPlaceholder?: string;
  sort: readonly DataViewSort[];
  onSortChange: (sort: DataViewSort[]) => void;
  filters?: ReactNode;
  actions?: ReactNode;
  activeFilterCount?: number;
  onResetFilters?: () => void;
  loading?: boolean;
  error?: ReactNode;
  onRetry?: () => void;
  emptyDescription?: ReactNode;
  /** Pointer convenience; provide a normal button/link in a cell for keyboard activation. */
  onRowClick?: (item: T) => void;
};

/** A controlled, compact server table. Rows are never sorted or filtered locally. */
export function CollectionDataView<T>({
  ariaLabel,
  columns,
  items,
  rowKey,
  total,
  page,
  pageSize,
  onPageChange,
  search,
  onSearchChange,
  searchPlaceholder = "Поиск",
  sort,
  onSortChange,
  filters,
  actions,
  activeFilterCount = 0,
  onResetFilters,
  loading = false,
  error,
  onRetry,
  emptyDescription = "Ничего не найдено",
  onRowClick,
}: DataViewProps<T>) {
  const table = useRef<HTMLTableElement>(null);
  const failed = error !== undefined && error !== null && error !== false;

  useEffect(() => {
    if (failed || loading) return;
    const element = table.current;
    if (!element || !onRowClick) return;
    // Native delegation preserves table semantics. The visible name button supplies keyboard access.
    function activate(event: MouseEvent) {
      if (event.button !== 0 || event.defaultPrevented || window.getSelection()?.toString().trim())
        return;
      const target = event.target;
      if (
        !(target instanceof Element) ||
        target.closest(
          "a,button,input,select,textarea,summary,[role='button'],[role='menuitem'],[contenteditable='true']",
        )
      )
        return;
      const row = target.closest<HTMLTableRowElement>("tr[data-row-id]");
      if (!row || !element?.contains(row)) return;
      const item = items.find((entry) => rowKey(entry) === row.dataset.rowId);
      if (item !== undefined) onRowClick?.(item);
    }
    element.addEventListener("click", activate);
    return () => element.removeEventListener("click", activate);
  }, [items, rowKey, onRowClick, failed, loading]);

  function toggleSort(key: string) {
    const selected = sort.find((entry) => entry.key === key);
    onSortChange(
      !selected
        ? [...sort, { key, direction: "asc" }]
        : selected.direction === "asc"
          ? sort.map((entry) => (entry.key === key ? { ...entry, direction: "desc" } : entry))
          : sort.filter((entry) => entry.key !== key),
    );
  }

  return (
    <section className="data-view" aria-label={ariaLabel}>
      <div className="data-view-toolbar">
        <Input
          type="search"
          className="data-view-search"
          allowClear
          value={search}
          onChange={(event) => onSearchChange(event.target.value)}
          placeholder={searchPlaceholder}
          aria-label={searchPlaceholder}
        />
        <div className="data-view-toolbar-actions">{actions}</div>
      </div>
      {(filters || activeFilterCount > 0) && (
        <div className="data-view-filterbar">
          {filters}
          {activeFilterCount > 0 && onResetFilters && (
            <Button type="text" onClick={onResetFilters}>
              Сбросить ({activeFilterCount})
            </Button>
          )}
        </div>
      )}
      <div className="data-view-results" aria-busy={loading}>
        {failed ? (
          <div className="data-view-state" role="alert">
            <div>{error}</div>
            {onRetry && <Button onClick={onRetry}>Повторить</Button>}
          </div>
        ) : loading && items.length === 0 ? (
          <div className="data-view-state" role="status">
            <Spin />
            <span>Загрузка…</span>
          </div>
        ) : items.length === 0 ? (
          <div className="data-view-state" role="status">
            {emptyDescription}
          </div>
        ) : (
          <>
            {loading && (
              <div className="data-view-updating" role="status">
                Обновление…
              </div>
            )}
            <section className="data-view-table-scroll" aria-label={`${ariaLabel}: таблица`}>
              <table
                ref={table}
                className={`data-view-table${onRowClick ? " data-view-table-interactive" : ""}`}
              >
                <caption className="data-view-sr-only">{ariaLabel}</caption>
                <thead>
                  <tr>
                    {columns.map((column) => {
                      const priority = sort.findIndex((entry) => entry.key === column.key);
                      const selected = sort[priority];
                      return (
                        <th
                          key={column.key}
                          scope="col"
                          aria-sort={
                            column.sortable
                              ? selected
                                ? priority === 0
                                  ? selected.direction === "asc"
                                    ? "ascending"
                                    : "descending"
                                  : "other"
                                : "none"
                              : undefined
                          }
                        >
                          <div className="data-view-heading">
                            <span>{column.title}</span>
                            {column.sortable && (
                              <button
                                type="button"
                                className={`data-view-sort${selected ? " data-view-sort-active" : ""}`}
                                aria-label={`Сортировка: ${column.title}`}
                                aria-pressed={Boolean(selected)}
                                title={
                                  selected
                                    ? `${selected.direction === "asc" ? "По возрастанию" : "По убыванию"}, приоритет ${priority + 1}`
                                    : "Сортировать по возрастанию"
                                }
                                onClick={() => toggleSort(column.key)}
                              >
                                <span aria-hidden="true">
                                  {selected ? (selected.direction === "asc" ? "↑" : "↓") : "↕"}
                                </span>
                                {selected && <sup aria-hidden="true">{priority + 1}</sup>}
                              </button>
                            )}
                          </div>
                        </th>
                      );
                    })}
                  </tr>
                </thead>
                <tbody>
                  {items.map((item) => (
                    <tr key={rowKey(item)} data-row-id={rowKey(item)}>
                      {columns.map((column) => (
                        <td key={column.key}>{column.render(item)}</td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </section>
          </>
        )}
      </div>
      {!failed && (
        <div className="data-view-pagination">
          <span className="data-view-count" aria-live="polite">
            {loading && items.length === 0
              ? ""
              : items.length === 0
                ? `Всего: ${total}`
                : `${(page - 1) * pageSize + 1}–${Math.min((page - 1) * pageSize + items.length, total)} из ${total}`}
          </span>
          <Pagination
            current={page}
            pageSize={pageSize}
            total={total}
            pageSizeOptions={[10, 20, 50, 100]}
            showSizeChanger
            responsive
            disabled={loading}
            onChange={(nextPage, nextSize) =>
              onPageChange(nextSize === pageSize ? nextPage : 1, nextSize)
            }
          />
        </div>
      )}
    </section>
  );
}
