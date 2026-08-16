import { Card, Typography } from "antd";
import type { ReactNode } from "react";

import styles from "./SummaryGrid.module.css";

export function SummaryGrid({ children }: { children: ReactNode }) {
  return <div className={styles.grid}>{children}</div>;
}

export function SummaryCard({ title, value, caption }: { title: ReactNode; value: ReactNode; caption?: ReactNode }) {
  return (
    <Card size="small">
      <Typography.Text type="secondary">{title}</Typography.Text>
      <div className={styles.value}>{value}</div>
      {caption ? <div className={styles.caption}>{caption}</div> : null}
    </Card>
  );
}
