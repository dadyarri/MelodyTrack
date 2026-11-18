import { useId } from "react";
import ReactPaginateImport from "react-paginate";
import { Icon } from "../Icon";
import styles from "./PaperPagination.module.css";
import { PaperSelect } from "./PaperSelect";

// The package's UMD entry exposes { default: Component } through Vite's CJS interop.
const ReactPaginate =
  (ReactPaginateImport as unknown as { default?: typeof ReactPaginateImport }).default ??
  ReactPaginateImport;

export function PaperPagination({
  page,
  pageSize,
  total,
  pageSizeOptions = [10, 20, 50, 100],
  disabled = false,
  onChange,
  ariaLabel = "Страницы",
  showPageSize = true,
}: {
  page: number;
  pageSize: number;
  total: number;
  pageSizeOptions?: number[];
  disabled?: boolean;
  onChange: (page: number, pageSize: number) => void;
  ariaLabel?: string;
  showPageSize?: boolean;
}) {
  const pageCount = Math.ceil(total / pageSize);
  const pageSizeId = useId();
  return (
    <div className={styles.root}>
      {pageCount > 1 && (
        <nav aria-label={ariaLabel}>
          <ReactPaginate
            forcePage={Math.min(page - 1, pageCount - 1)}
            pageCount={pageCount}
            marginPagesDisplayed={1}
            pageRangeDisplayed={3}
            pageLabelBuilder={(pageNumber) => String(pageNumber)}
            onPageChange={({ selected }) => !disabled && onChange(selected + 1, pageSize)}
            onClick={() => (disabled ? false : undefined)}
            previousLabel={<Icon name="previous" />}
            nextLabel={<Icon name="next" />}
            previousAriaLabel="Предыдущая страница"
            nextAriaLabel="Следующая страница"
            breakAriaLabels={{ forward: "Перейти вперёд", backward: "Перейти назад" }}
            ariaLabelBuilder={(index) => `Страница ${index}`}
            containerClassName={styles.pages}
            pageClassName={styles.page}
            pageLinkClassName={styles.link}
            activeClassName={styles.active}
            activeLinkClassName={styles.activeLink}
            previousClassName={styles.page}
            nextClassName={styles.page}
            previousLinkClassName={styles.link}
            nextLinkClassName={styles.link}
            disabledClassName={styles.disabled}
            breakClassName={styles.page}
            breakLinkClassName={styles.link}
            renderOnZeroPageCount={null}
          />
        </nav>
      )}
      {showPageSize && (
        <label className={styles.sizeLabel} htmlFor={pageSizeId}>
          <span>Строк на странице</span>
          <PaperSelect
            id={pageSizeId}
            aria-label="Строк на странице"
            value={String(pageSize)}
            disabled={disabled}
            options={pageSizeOptions.map((size) => ({
              value: String(size),
              label: `${size} на странице`,
            }))}
            onValueChange={(value) => onChange(1, Number(value))}
          />
        </label>
      )}
    </div>
  );
}
