import { Badge, Button, Empty, Popover, Space, Spin, Typography } from "antd";
import dayjs from "dayjs";
import type { ReactNode } from "react";
import { useNavigate } from "react-router";

import { BellOutlined } from "@/shared/ui/icons";

import type { AppNotification } from "../model/types";
import { useNotifications } from "../model/useNotifications";
import styles from "./NotificationBell.module.css";

export function NotificationBell({ footer }: { footer?: ReactNode }) {
  const navigate = useNavigate();
  const { items, unreadCount, loading, refetch, markRead, markAllRead, markingAllRead } = useNotifications();

  const openNotification = (notification: AppNotification) => {
    if (!notification.readAtUtc) {
      markRead(notification.id);
    }

    if (notification.deepLink?.startsWith("/") && !notification.deepLink.startsWith("//")) {
      void navigate(notification.deepLink);
    }
  };

  return (
    <Popover
      trigger="click"
      placement="bottomRight"
      classNames={{ root: styles.popover }}
      onOpenChange={(open) => {
        if (open) {
          void refetch();
        }
      }}
      content={
        <section className={styles.panel} aria-label="Уведомления">
          <div className={styles.panelHeader}>
            <Typography.Text strong>Уведомления</Typography.Text>
            {unreadCount > 0 ? (
              <Button
                type="link"
                size="small"
                loading={markingAllRead}
                onClick={() => {
                  markAllRead();
                }}
              >
                Прочитать все
              </Button>
            ) : null}
          </div>
          <div className={styles.list}>
            {loading ? (
              <Spin size="small" />
            ) : items.length === 0 ? (
              <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Новых уведомлений нет" />
            ) : (
              items.map((notification) => (
                <button
                  key={notification.id}
                  type="button"
                  className={`${styles.item}${notification.readAtUtc ? "" : ` ${styles.unread}`}`}
                  onClick={() => {
                    openNotification(notification);
                  }}
                >
                  <Space orientation="vertical" size={4} align="start">
                    <Typography.Text strong={!notification.readAtUtc}>{notification.title}</Typography.Text>
                    <Typography.Text className={styles.summary}>{notification.summary}</Typography.Text>
                    <Typography.Text type="secondary" className={styles.timestamp}>
                      {dayjs(notification.createdAtUtc).format("DD.MM.YYYY HH:mm")}
                    </Typography.Text>
                  </Space>
                </button>
              ))
            )}
          </div>
          {footer ? <div className={styles.footer}>{footer}</div> : null}
        </section>
      }
    >
      <Badge count={unreadCount} size="small" overflowCount={99}>
        <Button type="text" className={styles.trigger} icon={<BellOutlined />} aria-label="Открыть уведомления" />
      </Badge>
    </Popover>
  );
}
