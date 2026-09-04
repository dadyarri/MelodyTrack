import { useMutation, useQuery } from "@tanstack/react-query";
import { App as AntdApp, Button, Card, Space, Tag, Tooltip, Typography } from "antd";
import dayjs from "dayjs";
import { useEffect, useMemo } from "react";

import { getAppointmentStatusLabel, getAppointmentStatusTagColor } from "@/entities/appointment";
import { clientsApi } from "@/entities/client";
import { useAuth } from "@/entities/session";
import { clientPortalApi, type ClientPortalAppointment, clientPortalQueryKeys } from "@/features/client-portal";
import { formatMoney } from "@/shared/lib";
import { UrlCopyModal, useUrlCopyModal } from "@/shared/ui";
import { CalendarCheckOutlined } from "@/shared/ui/icons";

import styles from "./ClientPortalSchedulePage.module.css";

export function ClientPortalSchedulePage() {
  const auth = useAuth();
  const { message } = AntdApp.useApp();
  const timezone = useMemo(() => Intl.DateTimeFormat().resolvedOptions().timeZone, []);
  const linkedClientId = auth.user?.linkedClientId ?? null;
  const urlModal = useUrlCopyModal(auth.user?.id);

  const query = useQuery({
    queryKey: clientPortalQueryKeys.schedule(linkedClientId, timezone),
    queryFn: () => clientPortalApi.schedule({ timezone }),
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
  const balanceToneClassName = balance < 0 ? styles.balanceNegative : balance > 0 ? styles.balancePositive : styles.balanceNeutral;

  return (
    <>
      <Space vertical size={16} className={styles.stack}>
        <div className={styles.summaryGrid}>
          <Card loading={query.isLoading} className={styles.heroCard} title="Ближайшее занятие">
            <Space vertical size={10} className={styles.heroCardContent}>
              {nextAppointment ? (
                <>
                  <Typography.Text strong>{formatDateRange(nextAppointment.startDate, nextAppointment.endDate)}</Typography.Text>
                  <Space wrap>
                    <Tag color={getAppointmentStatusTagColor(nextAppointment.status)}>
                      {getAppointmentStatusLabel(nextAppointment.status)}
                    </Tag>
                    {nextAppointment.isTrial ? <Tag color="purple">Пробное занятие</Tag> : null}
                    {nextAppointment.courseTheme ? <Tag color="blue">Тема: {nextAppointment.courseTheme.title}</Tag> : null}
                  </Space>
                </>
              ) : (
                <Typography.Text type="secondary">Пока нет запланированных занятий.</Typography.Text>
              )}
              {query.isError ? (
                <Typography.Text type="warning">
                  {nextAppointment
                    ? "Не удалось обновить расписание. Показаны последние подтверждённые данные."
                    : "Не удалось обновить расписание."}
                </Typography.Text>
              ) : null}
            </Space>
          </Card>

          <Card className={styles.heroCard} title="Баланс">
            <Space vertical size={10} className={styles.heroCardContent}>
              <Typography.Title level={3} className={balanceToneClassName}>
                {formatMoney(balance)}
              </Typography.Title>
              {/* <Typography.Text type="secondary">
              {balance < 0
                ? "Отрицательный баланс означает задолженность."
                : balance > 0
                  ? "Положительный баланс можно использовать для будущих занятий."
                  : "Баланс сейчас закрыт."}
            </Typography.Text> */}
            </Space>
          </Card>

          <Card className={styles.heroCard} title="Подписка на календарь">
            <Space vertical size={10} className={styles.heroCardContent}>
              <Typography.Text type="secondary">Добавьте занятия в свой календарь и получайте привычные напоминания.</Typography.Text>
              <Tooltip title="Вы можете добавить свои занятия в любой удобный календарь: Apple Calendar, Google Calendar и другие.">
                <Button
                  icon={<CalendarCheckOutlined />}
                  loading={calendarSubscriptionMutation.isPending}
                  onClick={() => {
                    if (linkedClientId) {
                      calendarSubscriptionMutation.mutate(linkedClientId);
                    }
                  }}
                >
                  Подписка на календарь
                </Button>
              </Tooltip>
            </Space>
          </Card>
        </div>
      </Space>
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
