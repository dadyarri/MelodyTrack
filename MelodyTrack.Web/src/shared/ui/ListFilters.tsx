import { Space } from "antd";
import type { ReactNode } from "react";

import styles from "./ListFilters.module.css";

export function ListFilters({ children }: { children: ReactNode }) {
  return (
    <Space orientation="vertical" size={16} className="wide">
      <div className={styles.stack}>{children}</div>
    </Space>
  );
}
