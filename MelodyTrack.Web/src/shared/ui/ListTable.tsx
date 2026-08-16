import { Table, type TableProps } from "antd";
import type { ReactNode } from "react";

import { ListQueryStatus, type ListQueryStatusProps } from "./ListQueryStatus";
import styles from "./ListTable.module.css";

type ListTableProps<RecordType extends object> = TableProps<RecordType> & {
  emptyText?: ReactNode;
  queryStatus?: ListQueryStatusProps;
};

export function ListTable<RecordType extends object>({
  emptyText = "Нет данных",
  queryStatus,
  locale,
  scroll,
  ...props
}: ListTableProps<RecordType>) {
  const showBackgroundRefresh = Boolean(queryStatus?.isFetching && !queryStatus.isError && !props.loading);

  return (
    <div className={styles.root}>
      {queryStatus?.isError ? <ListQueryStatus {...queryStatus} isFetching={false} /> : null}
      {showBackgroundRefresh ? (
        <div className={styles.backgroundStatus}>
          <ListQueryStatus {...queryStatus} isFetching />
        </div>
      ) : null}
      <Table<RecordType>
        {...props}
        scroll={scroll ?? { x: "max-content" }}
        locale={{
          ...locale,
          emptyText: locale?.emptyText ?? emptyText,
        }}
      />
    </div>
  );
}
