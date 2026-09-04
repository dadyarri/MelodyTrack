import { Card, Space, Typography } from "antd";
import type { ReactNode } from "react";

import styles from "./AuthStyles.module.css";

type AuthScreenLayoutProps = {
  title: string;
  description?: string;
  children: ReactNode;
};

export function AuthScreenLayout({ title, description, children }: AuthScreenLayoutProps) {
  return (
    <main className={styles.authScreen}>
      <Card className={styles.authCard}>
        <Space orientation="vertical" size={20} className="wide">
          <div>
            <Typography.Title level={1}>{title}</Typography.Title>
            {description && <Typography.Text type="secondary">{description}</Typography.Text>}
          </div>
          {children}
        </Space>
      </Card>
    </main>
  );
}
