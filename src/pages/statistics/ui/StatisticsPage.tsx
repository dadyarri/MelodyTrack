import { useQuery } from "@tanstack/react-query";
import { Button, Card, DatePicker, Result, Select, Space, Table, Tabs, Tag, Typography } from "antd";
import dayjs from "dayjs";
import type { ReactNode } from "react";
import { useSearchParams } from "react-router";

import {
  analyticsQueryKeys,
  type ClientsReport,
  dashboardApi,
  type FinanceReport,
  parseReportParams,
  type ReportGroupBy,
  type ReportParams,
  serializeReportParams,
  type WorkReport,
} from "@/entities/dashboard";
import { DATE_FORMAT, formatDate, formatDateTime, formatMoney } from "@/shared/lib";
import { InfoLabel, ListFilters, PageLayout, SummaryCard, SummaryGrid } from "@/shared/ui";
import { STATS_CHART_COLORS, StatsDonutChart, StatsHorizontalBarChart, StatsTrendChart } from "@/shared/ui/charts";
import { filterFieldClassName } from "@/shared/ui/filterFieldStyles";

type StatisticsArea = "work" | "finance" | "clients";
type ReportData = WorkReport | FinanceReport | ClientsReport;

const groupingOptions: Array<{ label: string; value: ReportGroupBy }> = [
  { label: "По дням", value: "day" },
  { label: "По неделям", value: "week" },
  { label: "По месяцам", value: "month" },
];

export function StatisticsWorkPage() {
  return <StatisticsPage area="work" />;
}

export function StatisticsFinancePage() {
  return <StatisticsPage area="finance" />;
}

export function StatisticsClientsPage() {
  return <StatisticsPage area="clients" />;
}

function StatisticsPage({ area }: { area: StatisticsArea }) {
  const [search, setSearch] = useSearchParams();
  const params = parseReportParams(search);
  const query = useQuery({
    queryKey: analyticsQueryKeys.report(area, params),
    queryFn: () => loadReport(area, params),
  });
  const context = query.data?.context;

  const updateParams = (next: Partial<ReportParams>) => {
    const updated = { ...params, ...next };
    setSearch(serializeReportParams(updated), { replace: true });
  };

  return (
    <PageLayout title="Статистика" description="Данные о работе, деньгах и клиентах за выбранный период.">
      <ListFilters>
        <div className={filterFieldClassName}>
          <Typography.Text type="secondary">Период</Typography.Text>
          <DatePicker.RangePicker
            value={[dayjs(params.start), dayjs(params.end)]}
            format={DATE_FORMAT}
            onChange={(value) => {
              if (value?.[0] && value[1]) {
                updateParams({ start: value[0].format("YYYY-MM-DD"), end: value[1].format("YYYY-MM-DD") });
              }
            }}
          />
        </div>
        <div className={filterFieldClassName}>
          <Typography.Text type="secondary">Группировка</Typography.Text>
          <Select
            className="wide"
            value={params.groupBy}
            options={groupingOptions}
            onChange={(groupBy) => {
              updateParams({ groupBy });
            }}
          />
        </div>
        <div className={filterFieldClassName}>
          <Typography.Text type="secondary">Преподаватель</Typography.Text>
          <Select
            allowClear
            className="wide"
            placeholder="Все преподаватели"
            value={params.providerId}
            loading={query.isLoading}
            options={context?.providers.map((provider) => ({ label: provider.displayName, value: provider.id }))}
            onChange={(providerId) => {
              updateParams({ providerId });
            }}
          />
        </div>
      </ListFilters>

      <div>
        <Typography.Text type="secondary">Сейчас показано: </Typography.Text>
        <Tag color="blue">{context?.scopeLabel ?? "Вся организация"}</Tag>
      </div>

      {query.isError ? (
        <Result
          status="error"
          title="Не удалось загрузить статистику"
          subTitle="Попробуйте обновить данные. Выбранные фильтры сохранены."
          extra={<Button onClick={() => void query.refetch()}>Повторить</Button>}
        />
      ) : (
        <div data-onboarding-id="statistics-main">
          {area === "work" ? <WorkContent report={query.data as WorkReport | undefined} loading={query.isLoading} /> : null}
          {area === "finance" ? <FinanceContent report={query.data as FinanceReport | undefined} loading={query.isLoading} /> : null}
          {area === "clients" ? <ClientsContent report={query.data as ClientsReport | undefined} loading={query.isLoading} /> : null}
        </div>
      )}
    </PageLayout>
  );
}

function WorkContent({ report, loading }: { report?: WorkReport; loading: boolean }) {
  const summary = report?.summary;
  return (
    <ReportStack>
      <SummaryGrid>
        <SummaryCard
          title={<InfoLabel label="Всего записей" tooltip="Все неудалённые записи, начавшиеся в выбранном периоде." />}
          value={summary?.appointments ?? 0}
        />
        <SummaryCard
          title={<InfoLabel label="Проведено" tooltip="Записи с текущим статусом «Проведено»." />}
          value={summary?.completed ?? 0}
        />
        <SummaryCard
          title={<InfoLabel label="Рабочая ёмкость" tooltip="Рабочее время преподавателей по расписанию за вычетом отпусков." />}
          value={formatHours(summary?.workingCapacityHours)}
        />
        <SummaryCard
          title={
            <InfoLabel
              label="Занято в рабочее время"
              tooltip="Уникальное время записей внутри рабочего расписания; пересечения не считаются дважды."
            />
          }
          value={formatHours(summary?.occupiedWorkingHours)}
        />
        <SummaryCard
          title={<InfoLabel label="Свободно в рабочее время" tooltip="Рабочая ёмкость минус уникальное занятое рабочее время." />}
          value={formatHours(summary?.freeWorkingHours)}
        />
        <SummaryCard
          title={<InfoLabel label="Использование ёмкости" tooltip="Занятое рабочее время относительно рабочей ёмкости." />}
          value={formatPercent(summary?.utilizationPercent)}
        />
        <SummaryCard
          title={<InfoLabel label="Доля отмен" tooltip="Отменённые записи относительно всех записей периода." />}
          value={formatPercent(summary?.cancellationPercent)}
        />
      </SummaryGrid>
      <ChartGrid>
        <SectionCard title="Динамика работы">
          <StatsTrendChart
            data={report?.trend.map((item) => ({
              key: item.startDate,
              label: formatBucket(item.startDate, item.endDate),
              values: { occupied: item.occupiedWorkingHours, free: item.freeWorkingHours },
              tooltip: `Занято ${formatHours(item.occupiedWorkingHours)}, свободно ${formatHours(item.freeWorkingHours)}, ёмкость ${formatHours(item.workingCapacityHours)}`,
            }))}
            series={[
              { key: "occupied", label: "Занято в рабочее время", color: STATS_CHART_COLORS[0] },
              { key: "free", label: "Свободно в рабочее время", color: STATS_CHART_COLORS[2] },
            ]}
          />
        </SectionCard>
        <SectionCard title="Статусы записей">
          <StatsDonutChart
            items={report?.statuses.map((item, index) => ({
              key: item.status,
              label: statusLabels[item.status],
              value: item.count,
              valueLabel: String(item.count),
              shareLabel: formatPercent(item.sharePercent),
              color: STATS_CHART_COLORS[index % STATS_CHART_COLORS.length],
            }))}
            totalLabel="Записей"
            totalValueLabel={String(summary?.appointments ?? 0)}
          />
        </SectionCard>
      </ChartGrid>
      <SectionCard title="Подробности">
        <Tabs
          items={[
            {
              key: "providers",
              label: "Преподаватели",
              children: (
                <Table
                  rowKey={(row) => row.providerId ?? row.providerName}
                  loading={loading}
                  dataSource={report?.providers}
                  pagination={{ pageSize: 8 }}
                  scroll={{ x: "max-content" }}
                  columns={[
                    { title: "Преподаватель", dataIndex: "providerName" },
                    { title: "Записей", dataIndex: "appointments" },
                    { title: "Проведено", dataIndex: "completed" },
                    { title: "Отменено", dataIndex: "cancelled" },
                    { title: "Сгорело", dataIndex: "burned" },
                    { title: "Рабочая ёмкость", dataIndex: "workingCapacityHours", render: formatHours },
                    { title: "Занято в рабочее время", dataIndex: "occupiedWorkingHours", render: formatHours },
                    { title: "Свободно в рабочее время", dataIndex: "freeWorkingHours", render: formatHours },
                    { title: "Использование ёмкости", dataIndex: "utilizationPercent", render: formatPercent },
                  ]}
                />
              ),
            },
            {
              key: "services",
              label: "Услуги",
              children: (
                <Table
                  rowKey="serviceId"
                  loading={loading}
                  dataSource={report?.services}
                  pagination={{ pageSize: 8 }}
                  scroll={{ x: "max-content" }}
                  columns={[
                    { title: "Услуга", dataIndex: "serviceName" },
                    { title: "Записей", dataIndex: "appointments" },
                    { title: "Проведено", dataIndex: "completed" },
                    { title: "Сгорело", dataIndex: "burned" },
                    { title: "Выручка", dataIndex: "revenue", render: formatMoney },
                  ]}
                />
              ),
            },
            {
              key: "hours",
              label: "Часы",
              children: (
                <StatsHorizontalBarChart
                  items={report?.busyHours.map((item) => ({
                    key: String(item.hour),
                    label: `${String(item.hour).padStart(2, "0")}:00`,
                    value: item.appointments,
                    valueLabel: String(item.appointments),
                    tooltip: `Проведено: ${String(item.completed)}, отменено: ${String(item.cancelled)}`,
                  }))}
                />
              ),
            },
          ]}
        />
      </SectionCard>
    </ReportStack>
  );
}

function FinanceContent({ report, loading }: { report?: FinanceReport; loading: boolean }) {
  const summary = report?.summary;
  const organizationFigures = summary?.organizationOnlyFiguresAvailable ?? false;
  return (
    <ReportStack>
      <SummaryGrid>
        <SummaryCard
          title={<InfoLabel label="Выручка по занятиям" tooltip="Историческая цена проведённых и сгоревших непробных занятий периода." />}
          value={formatMoney(summary?.revenue)}
        />
        <SummaryCard
          title={
            <InfoLabel
              label="Фактические платежи"
              tooltip="Платёжные операции по дате получения; они могут погашать долг другого периода."
            />
          }
          value={formatOptionalMoney(summary?.payments)}
        />
        <SummaryCard
          title={<InfoLabel label="Расходы" tooltip="Расходные операции по дате совершения." />}
          value={formatOptionalMoney(summary?.expenses)}
        />
        <SummaryCard
          title={<InfoLabel label="Чистая прибыль" tooltip="Выручка по занятиям минус расходы периода." />}
          value={formatOptionalMoney(summary?.netProfit)}
        />
        <SummaryCard
          title={
            <InfoLabel label="Долг клиентов" tooltip="Положительная разница всех начислений и платежей каждого клиента на конец периода." />
          }
          value={formatOptionalMoney(summary?.outstandingDebt)}
        />
        <SummaryCard
          title={
            <InfoLabel
              label="Средняя выручка за занятие"
              tooltip="Выручка по непробным состоявшимся занятиям, делённая на их количество."
            />
          }
          value={formatOptionalMoney(summary?.averageRevenuePerVisit)}
        />
      </SummaryGrid>
      {!organizationFigures && !loading ? (
        <Typography.Text type="secondary">
          Платежи, расходы, прибыль и долг показаны только без фильтра преподавателя: эти записи нельзя достоверно связать с отдельным
          преподавателем.
        </Typography.Text>
      ) : null}
      <ChartGrid>
        <SectionCard title="Движение денег">
          <StatsTrendChart
            data={report?.trend.map((item) => ({
              key: item.startDate,
              label: formatBucket(item.startDate, item.endDate),
              values: {
                revenue: item.revenue,
                payments: item.payments ?? 0,
                expenses: item.expenses ?? 0,
              },
              tooltip: `Выручка ${formatMoney(item.revenue)}${organizationFigures ? `, платежи ${formatMoney(item.payments)}, расходы ${formatMoney(item.expenses)}` : ""}`,
            }))}
            series={
              organizationFigures
                ? [
                    { key: "revenue", label: "Выручка по занятиям", color: STATS_CHART_COLORS[0] },
                    { key: "payments", label: "Получено платежей", color: STATS_CHART_COLORS[2] },
                    { key: "expenses", label: "Расходы", color: STATS_CHART_COLORS[1] },
                  ]
                : [{ key: "revenue", label: "Выручка по занятиям", color: STATS_CHART_COLORS[0] }]
            }
          />
        </SectionCard>
        <SectionCard title="Расходы по статьям">
          <StatsDonutChart
            items={report?.expenseCategories.map((item, index) => ({
              key: item.categoryName,
              label: item.categoryName,
              value: item.amount,
              valueLabel: formatMoney(item.amount),
              color: STATS_CHART_COLORS[index % STATS_CHART_COLORS.length],
            }))}
            totalLabel="Расходы"
            totalValueLabel={formatMoney(summary?.expenses)}
            emptyText={organizationFigures ? "За этот период расходов нет." : "Доступно для всей организации."}
          />
        </SectionCard>
      </ChartGrid>
      <SectionCard title="Подробности">
        <Tabs
          items={[
            {
              key: "services",
              label: "Услуги",
              children: (
                <Table
                  rowKey="serviceId"
                  loading={loading}
                  dataSource={report?.services}
                  pagination={{ pageSize: 8 }}
                  scroll={{ x: "max-content" }}
                  columns={[
                    { title: "Услуга", dataIndex: "serviceName" },
                    { title: "Платных записей", dataIndex: "appointments" },
                    { title: "Выручка", dataIndex: "revenue", render: formatMoney },
                  ]}
                />
              ),
            },
            {
              key: "debt",
              label: "Долги",
              disabled: !organizationFigures,
              children: (
                <Table
                  rowKey="clientId"
                  loading={loading}
                  dataSource={report?.debtors}
                  pagination={{ pageSize: 8 }}
                  scroll={{ x: "max-content" }}
                  columns={[
                    { title: "Клиент", dataIndex: "clientName" },
                    { title: "Начислено", dataIndex: "revenue", render: formatMoney },
                    { title: "Оплачено", dataIndex: "payments", render: formatMoney },
                    { title: "Долг", dataIndex: "debt", render: formatMoney },
                  ]}
                />
              ),
            },
            {
              key: "expenses",
              label: "Расходы",
              disabled: !organizationFigures,
              children: (
                <Table
                  rowKey="categoryName"
                  loading={loading}
                  dataSource={report?.expenseCategories}
                  pagination={false}
                  columns={[
                    { title: "Статья", dataIndex: "categoryName" },
                    { title: "Сумма", dataIndex: "amount", render: formatMoney },
                  ]}
                />
              ),
            },
          ]}
        />
      </SectionCard>
    </ReportStack>
  );
}

function ClientsContent({ report, loading }: { report?: ClientsReport; loading: boolean }) {
  const summary = report?.summary;
  return (
    <ReportStack>
      <SummaryGrid>
        <SummaryCard
          title={
            <InfoLabel label="Новые клиенты" tooltip="Клиенты, чьё первое состоявшееся непробное занятие пришлось на выбранный период." />
          }
          value={summary?.acquiredClients ?? 0}
        />
        <SummaryCard
          title={
            <InfoLabel label="Активные клиенты" tooltip="Клиенты хотя бы с одним проведённым или сгоревшим непробным занятием в периоде." />
          }
          value={summary?.activeClients ?? 0}
        />
        <SummaryCard
          title={
            <InfoLabel
              label="Вернулись из прошлого периода"
              tooltip="Клиенты с посещениями и в выбранном, и в непосредственно предыдущем периоде той же длины."
            />
          }
          value={summary?.retainedClients ?? 0}
        />
        <SummaryCard
          title={<InfoLabel label="Удержание" tooltip="Вернувшиеся клиенты относительно активных клиентов прошлого периода." />}
          value={formatPercent(summary?.retentionPercent)}
        />
        <SummaryCard
          title={<InfoLabel label="Средняя частота посещений" tooltip="Состоявшиеся занятия периода на одного активного клиента." />}
          value={formatDecimal(summary?.averageVisitFrequency)}
        />
        <SummaryCard
          title={<InfoLabel label="Под риском" tooltip="Без посещений 31–60 дней; дни отпуска клиента не считаются." />}
          value={summary?.atRiskClients ?? 0}
        />
        <SummaryCard
          title={<InfoLabel label="Потеряны" tooltip="Без посещений больше 60 дней; дни отпуска клиента не считаются." />}
          value={summary?.lostClients ?? 0}
        />
        <SummaryCard
          title={<InfoLabel label="В отпуске" tooltip="Неактивные клиенты, чей отпуск включает последний день отчёта." />}
          value={summary?.onVacationClients ?? 0}
        />
        <SummaryCard
          title={
            <InfoLabel
              label="Средняя выручка клиента"
              tooltip="Средняя историческая выручка клиентов с положительной выручкой до конца периода."
            />
          }
          value={formatOptionalMoney(summary?.averageClientValue)}
        />
      </SummaryGrid>
      <ChartGrid>
        <SectionCard title="Активность клиентов">
          <StatsTrendChart
            data={report?.trend.map((item) => ({
              key: item.startDate,
              label: formatBucket(item.startDate, item.endDate),
              values: { clients: item.activeClients, visits: item.visits, acquired: item.acquiredClients },
              tooltip: `Активных: ${String(item.activeClients)}, визитов: ${String(item.visits)}, новых: ${String(item.acquiredClients)}`,
            }))}
            series={[
              { key: "clients", label: "Активные клиенты", color: STATS_CHART_COLORS[0] },
              { key: "visits", label: "Состоявшиеся занятия", color: STATS_CHART_COLORS[2] },
              { key: "acquired", label: "Новые клиенты", color: STATS_CHART_COLORS[3] },
            ]}
          />
        </SectionCard>
        <SectionCard title="Новые клиенты по источникам">
          <StatsDonutChart
            items={report?.sources.map((item, index) => ({
              key: item.sourceName,
              label: item.sourceName,
              value: item.acquiredClients,
              valueLabel: String(item.acquiredClients),
              color: STATS_CHART_COLORS[index % STATS_CHART_COLORS.length],
            }))}
            totalLabel="Новых"
            totalValueLabel={String(summary?.acquiredClients ?? 0)}
          />
        </SectionCard>
      </ChartGrid>
      <SectionCard title="Подробности">
        <Tabs
          items={[
            {
              key: "clients",
              label: "Клиенты",
              children: (
                <Table
                  rowKey="clientId"
                  loading={loading}
                  dataSource={report?.clients}
                  pagination={{ pageSize: 8 }}
                  scroll={{ x: "max-content" }}
                  columns={[
                    { title: "Клиент", dataIndex: "clientName" },
                    { title: "Источник", dataIndex: "sourceName" },
                    { title: "Посещений за всё время", dataIndex: "visits" },
                    { title: "Выручка за всё время", dataIndex: "value", render: formatMoney },
                    { title: "Средний календарный интервал", dataIndex: "averageIntervalDays", render: formatDays },
                    { title: "Последнее занятие", dataIndex: "lastVisitAtUtc", render: formatOptionalDateTime },
                    { title: "Состояние", dataIndex: "activityState", render: renderActivityState },
                  ]}
                />
              ),
            },
            {
              key: "sources",
              label: "Источники",
              children: (
                <Table
                  rowKey="sourceName"
                  loading={loading}
                  dataSource={report?.sources}
                  pagination={false}
                  scroll={{ x: "max-content" }}
                  columns={[
                    { title: "Источник", dataIndex: "sourceName" },
                    { title: "Новых клиентов", dataIndex: "acquiredClients" },
                    { title: "Активных", dataIndex: "activeClients" },
                    { title: "Выручка клиентов за всё время", dataIndex: "clientValue", render: formatMoney },
                  ]}
                />
              ),
            },
          ]}
        />
      </SectionCard>
    </ReportStack>
  );
}

function ReportStack({ children }: { children: ReactNode }) {
  return (
    <Space orientation="vertical" size={20} className="wide">
      {children}
    </Space>
  );
}

function ChartGrid({ children }: { children: ReactNode }) {
  return <div style={{ display: "grid", gap: 16, gridTemplateColumns: "repeat(auto-fit, minmax(min(420px, 100%), 1fr))" }}>{children}</div>;
}

function SectionCard({ title, children }: { title: ReactNode; children: ReactNode }) {
  return <Card title={title}>{children}</Card>;
}

function loadReport(area: StatisticsArea, params: ReportParams): Promise<ReportData> {
  switch (area) {
    case "finance":
      return dashboardApi.finance(params);
    case "clients":
      return dashboardApi.clients(params);
    default:
      return dashboardApi.work(params);
  }
}

const statusLabels = { planned: "Запланировано", completed: "Проведено", cancelled: "Отменено", burned: "Сгорело" } as const;
const activityLabels = {
  active: "Активен",
  inactive: "Неактивен",
  "at-risk": "Под риском",
  lost: "Потерян",
  "on-vacation": "В отпуске",
} as const;

function formatPercent(value?: number | null) {
  return value == null ? "—" : `${value.toLocaleString("ru-RU", { maximumFractionDigits: 1 })} %`;
}

function formatHours(value?: number | null) {
  return value == null ? "—" : `${value.toLocaleString("ru-RU", { maximumFractionDigits: 1 })} ч`;
}

function formatDays(value?: number | null) {
  return value == null ? "—" : `${value.toLocaleString("ru-RU", { maximumFractionDigits: 1 })} дн.`;
}

function formatDecimal(value?: number | null) {
  return value == null ? "—" : value.toLocaleString("ru-RU", { maximumFractionDigits: 1 });
}

function formatOptionalMoney(value?: number | null) {
  return value == null ? "—" : formatMoney(value);
}

function formatOptionalDateTime(value?: string | null) {
  return value ? formatDateTime(value) : "—";
}

function formatBucket(start: string, end: string) {
  return start === end ? formatDate(start) : `${formatDate(start)} — ${formatDate(end)}`;
}

function renderActivityState(value: keyof typeof activityLabels) {
  const color =
    value === "active" ? "green" : value === "at-risk" ? "orange" : value === "lost" ? "red" : value === "on-vacation" ? "blue" : "default";
  return <Tag color={color}>{activityLabels[value]}</Tag>;
}
