import { type ReactNode, useEffect, useRef } from "react";
import { Icon } from "@/shared/ui/Icon";
import { PaperButton, PaperPagination } from "@/shared/ui/paper";
import styles from "./data-view.module.css";

export type DataViewSort = { key: string; direction: "asc" | "desc" };
export type DataViewColumn<T> = {
  key: string;
  title: string;
  render: (item: T) => ReactNode;
  sortable?: boolean;
  sortDisabledReason?: string;
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
  /** Describes an empty failed read; errors are reported outside the table shell. */
  error?: ReactNode;
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
  emptyDescription = "Ничего не найдено",
  onRowClick,
}: DataViewProps<T>) {
  const table = useRef<HTMLTableElement>(null);

  useEffect(() => {
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
  }, [items, rowKey, onRowClick]);

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
    <section className={styles.dataView} aria-label={ariaLabel}>
      <div className={styles.toolbar}>
        <input
          type="search"
          className={styles.search}
          value={search}
          onChange={(event) => onSearchChange(event.target.value)}
          placeholder={searchPlaceholder}
          aria-label={searchPlaceholder}
        />
        <div className={styles.toolbarActions}>{actions}</div>
      </div>
      {(filters || activeFilterCount > 0) && (
        <div className={styles.filterbar}>
          {filters}
          {activeFilterCount > 0 && onResetFilters && (
            <PaperButton onClick={onResetFilters}>Сбросить ({activeFilterCount})</PaperButton>
          )}
        </div>
      )}
      <div className={styles.results} aria-busy={loading}>
        {loading && items.length === 0 ? (
          <div className={styles.state} role="status">
            <span className={styles.spinner} aria-hidden="true" />
            <span>Загрузка…</span>
          </div>
        ) : items.length === 0 ? (
          <div className={styles.state} role="status">
            {error ?? emptyDescription}
          </div>
        ) : (
          <>
            {loading && (
              <div className={styles.updating} role="status">
                <span className={styles.srOnly}>Обновление…</span>
              </div>
            )}
            <section className={styles.tableScroll} aria-label={`${ariaLabel}: таблица`}>
              <table
                ref={table}
                className={`${styles.table}${onRowClick ? ` ${styles.tableInteractive}` : ""}`}
              >
                <caption className={styles.srOnly}>{ariaLabel}</caption>
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
                          <div className={styles.heading}>
                            <span>{column.title}</span>
                            {column.sortable && (
                              <button
                                type="button"
                                className={`${styles.sort}${selected ? ` ${styles.sortActive}` : ""}`}
                                aria-label={`Сортировка: ${column.title}`}
                                aria-pressed={Boolean(selected)}
                                disabled={Boolean(column.sortDisabledReason)}
                                title={
                                  column.sortDisabledReason ??
                                  (selected
                                    ? `${selected.direction === "asc" ? "По возрастанию" : "По убыванию"}, приоритет ${priority + 1}`
                                    : "Сортировать по возрастанию")
                                }
                                onClick={() => toggleSort(column.key)}
                              >
                                <Icon
                                  name={
                                    selected
                                      ? selected.direction === "asc"
                                        ? "sortAscending"
                                        : "sortDescending"
                                      : "sort"
                                  }
                                  size={18}
                                />
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
      <div className={styles.pagination}>
        <span className={styles.count} aria-live="polite">
          {loading && items.length === 0
            ? ""
            : items.length === 0
              ? `Всего: ${total}`
              : `${(page - 1) * pageSize + 1}–${Math.min((page - 1) * pageSize + items.length, total)} из ${total}`}
        </span>
        <PaperPagination
          page={page}
          pageSize={pageSize}
          total={total}
          disabled={loading}
          onChange={onPageChange}
        />
      </div>
    </section>
  );
}
