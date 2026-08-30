import { Alert, Button, Space } from "antd";

import type { SystemNotice } from "../model/types";
import { useSystemNotices } from "../model/useSystemNotices";
import styles from "./SystemNoticeCenter.module.css";

export function SystemNoticeCenter({ preAuth = false }: { preAuth?: boolean }) {
  const { notices, dismiss, dismissing } = useSystemNotices(preAuth);

  if (notices.length === 0) {
    return null;
  }

  return (
    <Space orientation="vertical" size={12} className={styles.stack} aria-label="Системные уведомления">
      {notices.map((notice) => (
        <Alert
          key={notice.id}
          className={`${styles.notice}${notice.severity === "critical" ? ` ${styles.critical}` : ""}`}
          type={toAlertType(notice.severity)}
          showIcon
          title={notice.title}
          description={notice.body}
          action={
            !preAuth && notice.dismissible ? (
              <Button
                size="small"
                disabled={dismissing}
                onClick={() => {
                  dismiss(notice.id);
                }}
              >
                Скрыть
              </Button>
            ) : null
          }
        />
      ))}
    </Space>
  );
}

function toAlertType(severity: SystemNotice["severity"]): "info" | "success" | "warning" | "error" {
  if (severity === "success") {
    return "success";
  }
  if (severity === "warning") {
    return "warning";
  }
  if (severity === "critical") {
    return "error";
  }
  return "info";
}
