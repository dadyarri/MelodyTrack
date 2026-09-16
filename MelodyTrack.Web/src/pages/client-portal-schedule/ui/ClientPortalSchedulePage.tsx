import { useMutation, useQuery } from "@tanstack/react-query";
import { App as AntdApp } from "antd";
import dayjs from "dayjs";
import { useEffect, useMemo } from "react";

import { getAppointmentStatusLabel } from "@/entities/appointment";
import { clientsApi } from "@/entities/client";
import { useAuth } from "@/entities/session";
import { clientPortalApi, type ClientPortalAppointment, clientPortalQueryKeys } from "@/features/client-portal";
import { formatMoney } from "@/shared/lib";
import { UrlCopyModal, useUrlCopyModal } from "@/shared/ui";
import { RpgApiError, RpgBadge, RpgButton, RpgPanel, RpgState } from "@/shared/ui/rpg";

import styles from "./ClientPortalSchedulePage.module.css";

export function ClientPortalSchedulePage() {
  const auth = useAuth();
  const { message } = AntdApp.useApp();
  const timezone = useMemo(() => Intl.DateTimeFormat().resolvedOptions().timeZone, []);
  const linkedClientId = auth.user?.linkedClientId ?? null;
  const urlModal = useUrlCopyModal(auth.user?.id);

  const query = useQuery({
    queryKey: clientPortalQueryKeys.schedule(linkedClientId, timezone),
    queryFn: ({ signal }) => clientPortalApi.schedule({ timezone }, signal),
    enabled: Boolean(linkedClientId),
    refetchInterval: (currentQuery) => getNextRefreshInterval(currentQuery.state.data),
    refetchIntervalInBackground: false,
  });
  const { refetch } = query;

  useEffect(() => {
    const refreshWhenVisible = () => {
      if (document.visibilityState === "visible") {
        void refetch();
      }
    };

    window.addEventListener("focus", refreshWhenVisible);
    window.addEventListener("online", refreshWhenVisible);
    document.addEventListener("visibilitychange", refreshWhenVisible);
    return () => {
      window.removeEventListener("focus", refreshWhenVisible);
      window.removeEventListener("online", refreshWhenVisible);
      document.removeEventListener("visibilitychange", refreshWhenVisible);
    };
  }, [refetch]);
  const calendarSubscriptionMutation = useMutation({
    mutationFn: (clientId: string) => clientsApi.regenerateCalendarSubscription(clientId),
    onSuccess: (subscription) => {
      urlModal.openUrlModal({
        url: subscription.url,
        title: "Подписка на календарь",
        description: "Скопируйте ссылку и добавьте её в приложение календаря.",
        warning: "Предыдущая ссылка на календарь уже отключена.",
      });
      message.success("Ссылка на календарь создана");
    },
  });

  const nextAppointment = query.data;
  const balance = auth.user?.balance ?? 0;
  return (
    <>
      <div className={styles.stack}>
        <h1>Расписание</h1>
        <div className={styles.summaryGrid}>
          <RpgPanel className={styles.stack}>
            <h2>Ближайшее занятие</h2>
            {query.isPending ? <RpgState kind="loading" title="Загружаем расписание" /> : null}
            {query.isError ? (
              <RpgApiError
                error={query.error}
                onRetry={() => {
                  void refetch();
                }}
              />
            ) : null}
            {nextAppointment ? (
              <>
                {query.isError ? <p>Показаны последние подтверждённые данные.</p> : null}
                <strong>{formatDateRange(nextAppointment.startDate, nextAppointment.endDate)}</strong>
                <div className={styles.badges}>
                  <RpgBadge>{getAppointmentStatusLabel(nextAppointment.status)}</RpgBadge>
                  {nextAppointment.isTrial ? <RpgBadge>Пробное занятие</RpgBadge> : null}
                </div>
                {nextAppointment.courseTheme ? <p>Тема: {nextAppointment.courseTheme.title}</p> : null}
              </>
            ) : query.isSuccess ? (
              <p>Пока нет запланированных занятий.</p>
            ) : null}
          </RpgPanel>
          <RpgPanel className={styles.stack}>
            <h2>Баланс</h2>
            <p className={styles.balance} data-debt={balance < 0}>
              {formatMoney(balance)}
            </p>
          </RpgPanel>
          <RpgPanel className={styles.stack}>
            <h2>Подписка на календарь</h2>
            <p>Добавьте занятия в Apple Calendar, Google Calendar или другой удобный календарь.</p>
            <div>
              <RpgButton
                pending={calendarSubscriptionMutation.isPending}
                disabled={!linkedClientId}
                onClick={() => {
                  if (linkedClientId) calendarSubscriptionMutation.mutate(linkedClientId);
                }}
              >
                Подписка на календарь
              </RpgButton>
            </div>
            {calendarSubscriptionMutation.isError ? <RpgApiError error={calendarSubscriptionMutation.error} /> : null}
          </RpgPanel>
        </div>
      </div>
      <UrlCopyModal {...urlModal.urlModalProps} />
    </>
  );
}

function formatDateRange(startDate: string, endDate: string) {
  const start = dayjs(startDate);
  const end = dayjs(endDate);
  return `${start.format("D MMMM, dddd · HH:mm")} - ${end.format("HH:mm")}`;
}

function getNextRefreshInterval(nextAppointment: ClientPortalAppointment | null | undefined) {
  if (!nextAppointment) {
    return 60_000;
  }

  const untilEnd = new Date(nextAppointment.endDate).getTime() - Date.now();
  return Math.max(1_000, Math.min(untilEnd + 1, 60_000));
}
