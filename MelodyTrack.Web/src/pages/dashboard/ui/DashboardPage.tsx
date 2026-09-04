import { Button, Card, Empty, Skeleton, Space, Statistic, Table, Tag, Typography } from "antd";
import dayjs from "dayjs";

import { renderAppointmentStatusTag } from "@/entities/appointment";
import { type Client, getPhoneUri, getSocialLinkHref } from "@/entities/client";
import type { DashboardAppointment, DashboardScheduleDay, DashboardStats } from "@/entities/dashboard";
import { formatMoney, TIME_FORMAT } from "@/shared/lib";
import { PageLayout, ShortcutButton } from "@/shared/ui";
import { DownloadOutlined, LinkOutlined, PhoneOutlined, SendOutlined } from "@/shared/ui/icons";
import tableLinkButtonStyles from "@/shared/ui/TableLinkButton.module.css";
import { ClientHistoryDrawer } from "@/widgets/client-history";

import { useDashboardPageController } from "../model/useDashboardPageController";
import styles from "./DashboardPage.module.css";

type DashboardContentProps = {
  data?: DashboardStats;
  isLoading: boolean;
  isError: boolean;
  onRetry: () => void;
  canSeeOrganization?: boolean;
  debtors?: Client[];
  isDebtorsLoading?: boolean;
  isExportingDebtors?: boolean;
  onExportDebtors?: () => void;
  onSelectDebtor?: (client: Client) => void;
};

export function DashboardPage() {
  const controller = useDashboardPageController();

  return (
    <>
      <DashboardContent
        data={controller.dashboardQuery.data}
        isLoading={controller.dashboardQuery.isLoading}
        isError={controller.dashboardQuery.isError}
        canSeeOrganization={controller.canSeeOrganization}
        debtors={controller.debtorsQuery.data}
        isDebtorsLoading={controller.debtorsQuery.isLoading}
        isExportingDebtors={controller.debtorsExportMutation.isPending}
        onRetry={() => {
          void controller.retry();
        }}
        onExportDebtors={() => {
          controller.debtorsExportMutation.mutate();
        }}
        onSelectDebtor={controller.setHistoryClient}
      />
      <ClientHistoryDrawer
        client={controller.historyClient}
        data={controller.historyQuery.data}
        isLoading={controller.historyQuery.isLoading}
        error={controller.historyQuery.error}
        onClose={controller.closeHistoryClient}
        onCreateAppointment={controller.clientHistoryActions.onCreateAppointment}
        onCreatePayment={controller.clientHistoryActions.onCreatePayment}
        onEventsPageChange={controller.setHistoryEventsPage}
      />
    </>
  );
}

export function DashboardContent({
  data,
  isLoading,
  isError,
  onRetry,
  canSeeOrganization = false,
  debtors,
  isDebtorsLoading = false,
  isExportingDebtors = false,
  onExportDebtors,
  onSelectDebtor,
}: DashboardContentProps) {
  const organization = data?.organization;

  return (
    <PageLayout title="Обзор">
      <div className={styles.grid} data-onboarding-id="dashboard-content">
        {canSeeOrganization ? (
          <>
            <DashboardStatisticCard title="Записи сегодня" value={organization?.appointmentsToday} isLoading={isLoading} />
            <DashboardStatisticCard title="Записи завтра" value={organization?.appointmentsTomorrow} isLoading={isLoading} />
            <DashboardStatisticCard title="Должники" value={organization?.debtorsCount} isLoading={isLoading} />
            <DashboardStatisticCard title="Общий долг" value={formatOptionalMoney(organization?.totalDebt)} isLoading={isLoading} />
            <DashboardStatisticCard title="Всего клиентов" value={organization?.totalClients} isLoading={isLoading} />
            <DashboardStatisticCard
              title="Весь резерв"
              value={formatOptionalMoney(organization?.totalPositiveBalance)}
              isLoading={isLoading}
            />
            <DashboardStatisticCard title="Доход за месяц" value={formatOptionalMoney(organization?.monthIncome)} isLoading={isLoading} />
            <DashboardStatisticCard
              title="Расход за месяц"
              value={formatOptionalMoney(organization?.monthExpenses)}
              isLoading={isLoading}
            />
            <DashboardStatisticCard title="Итог за месяц" value={formatOptionalMoney(organization?.monthNet)} isLoading={isLoading} />
          </>
        ) : null}

        <DashboardDayCard
          className={styles.scheduleToday}
          title="Записи на сегодня"
          day={data?.today}
          fallbackDate={dayjs()}
          emptyDescription="На сегодня записей нет"
          isLoading={isLoading}
          isError={isError}
          onRetry={onRetry}
        />
        <DashboardDayCard
          className={styles.scheduleTomorrow}
          title="Записи на завтра"
          day={data?.tomorrow}
          fallbackDate={dayjs().add(1, "day")}
          emptyDescription="На завтра записей нет"
          isLoading={isLoading}
          isError={isError}
          onRetry={onRetry}
        />

        {canSeeOrganization ? (
          <Card
            data-onboarding-id="dashboard-debtors"
            className={styles.debtors}
            title="Клиенты с отрицательным балансом"
            extra={
              <ShortcutButton
                shortcut="X"
                leadingIcon={<DownloadOutlined />}
                loading={isExportingDebtors}
                label="Экспорт"
                onClick={onExportDebtors}
              />
            }
          >
            <Table
              rowKey="id"
              loading={isDebtorsLoading}
              dataSource={debtors}
              pagination={false}
              scroll={{ x: "max-content" }}
              columns={[
                {
                  title: "Клиент",
                  render: (_, row) => (
                    <Button type="link" className={tableLinkButtonStyles.button} onClick={() => onSelectDebtor?.(row)}>
                      {`${row.lastName} ${row.firstName}`}
                    </Button>
                  ),
                },
                {
                  title: "Баланс",
                  dataIndex: "balance",
                  render: (value: number) => <Tag color="red">{formatMoney(value)}</Tag>,
                },
              ]}
            />
          </Card>
        ) : null}
      </div>
    </PageLayout>
  );
}

function DashboardStatisticCard({ title, value, isLoading }: { title: string; value?: number | string; isLoading: boolean }) {
  return (
    <Card className={styles.statisticCard} size="small" title={title}>
      <Statistic value={value ?? "—"} loading={isLoading && value === undefined} />
    </Card>
  );
}

function formatOptionalMoney(value?: number) {
  return value === undefined ? undefined : formatMoney(value);
}

function DashboardDayCard({
  className,
  title,
  day,
  fallbackDate,
  emptyDescription,
  isLoading,
  isError,
  onRetry,
}: {
  className: string;
  title: string;
  day?: DashboardScheduleDay;
  fallbackDate: dayjs.Dayjs;
  emptyDescription: string;
  isLoading: boolean;
  isError: boolean;
  onRetry: () => void;
}) {
  const date = day ? dayjs(day.date) : fallbackDate;

  return (
    <Card className={`${styles.schedule} ${className}`} title={`${title}, ${formatDateTitle(date)}`}>
      {isLoading && !day ? (
        <Skeleton active paragraph={{ rows: 2 }} title={false} />
      ) : isError && !day ? (
        <Space orientation="vertical" size={8} className={styles.queryState}>
          <Typography.Text type="danger">Не удалось загрузить записи.</Typography.Text>
          <Button size="small" onClick={onRetry}>
            Повторить
          </Button>
        </Space>
      ) : (
        <ReminderList appointments={day?.appointments ?? []} emptyDescription={emptyDescription} />
      )}
    </Card>
  );
}

function ReminderList({ appointments, emptyDescription }: { appointments: DashboardAppointment[]; emptyDescription: string }) {
  if (appointments.length === 0) {
    return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={emptyDescription} />;
  }

  return (
    <ul className={styles.appointmentList}>
      {appointments.map((appointment) => (
        <DashboardScheduleItem key={appointment.id} appointment={appointment} />
      ))}
    </ul>
  );
}

export function DashboardScheduleItem({ appointment }: { appointment: DashboardAppointment }) {
  const start = dayjs(appointment.startDate);
  const status = renderAppointmentStatusTag(appointment.status);
  const telegramHref = getSocialLinkHref(appointment.client.contacts?.telegram, "telegram");
  const vkHref = getSocialLinkHref(appointment.client.contacts?.vk, "vk");

  return (
    <li className={styles.appointment}>
      <Space orientation="vertical" size={2} className="wide">
        <Space wrap align="start" className="wide" style={{ justifyContent: "space-between" }}>
          <Space wrap>
            <Typography.Text strong>{start.format(TIME_FORMAT)}</Typography.Text>
            {status}
          </Space>
          <Space size={4}>
            {appointment.client.contacts?.phone ? (
              <Button
                shape="circle"
                size="small"
                icon={<PhoneOutlined />}
                href={getPhoneUri(appointment.client.contacts.phone)}
                title={appointment.client.contacts.phone}
              />
            ) : null}
            {telegramHref ? (
              <Button
                shape="circle"
                size="small"
                icon={<SendOutlined />}
                href={telegramHref}
                target="_blank"
                rel="noreferrer"
                title="Telegram"
              />
            ) : null}
            {vkHref ? (
              <Button shape="circle" size="small" icon={<LinkOutlined />} href={vkHref} target="_blank" rel="noreferrer" title="VK" />
            ) : null}
          </Space>
        </Space>
        <Typography.Text>
          {appointment.client.lastName} {appointment.client.firstName} — {appointment.service.name}
        </Typography.Text>
      </Space>
    </li>
  );
}

function formatDateTitle(value: dayjs.Dayjs) {
  return `${value.format("DD.MM.YYYY")} (${value.format("dd").toUpperCase()})`;
}
