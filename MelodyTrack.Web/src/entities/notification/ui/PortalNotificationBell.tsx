import dayjs from "dayjs";
import { type ReactNode, useState } from "react";
import { useNavigate } from "react-router";

import { BellOutlined } from "@/shared/ui/icons";
import { RpgApiError, RpgButton, RpgDialog, RpgState } from "@/shared/ui/rpg";

import { useNotifications } from "../model/useNotifications";
import styles from "./PortalNotificationBell.module.css";

export function PortalNotificationBell({ footer }: { footer?: ReactNode }) {
  const [open, setOpen] = useState(false);
  const notifications = useNotifications();
  const navigate = useNavigate();
  return (
    <>
      <RpgButton
        className={styles.trigger}
        aria-label={`Уведомления${notifications.unreadCount ? `: ${String(notifications.unreadCount)} непрочитанных` : ""}`}
        title="Уведомления"
        onClick={() => {
          setOpen(true);
          void notifications.refetch();
        }}
      >
        <BellOutlined />
        {notifications.unreadCount > 0 ? (
          <span className={styles.count} aria-hidden="true">
            {Math.min(notifications.unreadCount, 99)}
          </span>
        ) : null}
      </RpgButton>
      <RpgDialog
        open={open}
        title="Уведомления"
        onRequestClose={() => {
          setOpen(false);
        }}
        footer={footer}
      >
        <div className={styles.content}>
          {notifications.error ? (
            <RpgApiError
              error={notifications.error}
              onRetry={() => {
                void notifications.refetch();
              }}
            />
          ) : null}
          {notifications.unreadCount > 0 ? (
            <RpgButton
              variant="quiet"
              pending={notifications.markingAllRead}
              onClick={() => {
                notifications.markAllRead();
              }}
            >
              Прочитать все
            </RpgButton>
          ) : null}
          {notifications.loading ? (
            <RpgState kind="loading" title="Загружаем уведомления" />
          ) : !notifications.items.length && !notifications.error ? (
            <RpgState kind="empty" title="Новых уведомлений нет" />
          ) : null}
          {notifications.items.map((item) => (
            <button
              key={item.id}
              type="button"
              className={styles.item}
              onClick={() => {
                if (!item.readAtUtc) notifications.markRead(item.id);
                if (item.deepLink?.startsWith("/") && !item.deepLink.startsWith("//")) {
                  setOpen(false);
                  void navigate(item.deepLink);
                }
              }}
            >
              <strong>{item.title}</strong>
              <span>{item.summary}</span>
              <time dateTime={item.createdAtUtc}>{dayjs(item.createdAtUtc).format("DD.MM.YYYY HH:mm")}</time>
            </button>
          ))}
        </div>
      </RpgDialog>
    </>
  );
}
