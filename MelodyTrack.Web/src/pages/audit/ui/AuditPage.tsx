import { Input, Typography } from "antd";

import type { RecurringTaskType } from "@/entities/task";
import { getRecurringTaskTypeLabel } from "@/entities/task";
import { formatDateTime } from "@/shared/lib";
import { AccessDeniedNotice, ActionableEmptyState, ListFilters, ListTable, PageLayout } from "@/shared/ui";
import { filterFieldWideClassName } from "@/shared/ui/filterFieldStyles";

import { useAuditPageController } from "../model/useAuditPageController";
import styles from "./AuditPage.module.css";

const recurringTaskAuditTypes = new Set<RecurringTaskType>([
  "appointment-reminder",
  "birthday-greeting",
  "trial-follow-up",
  "inactive-client-reminder",
  "teacher-daily-schedule",
  "debtor-reminder",
  "custom-task",
]);

function formatActorLabel(displayName?: string | null, email?: string | null) {
  if (displayName?.trim()) {
    return displayName.trim().split(/\s+/).slice(0, 2).join(" ");
  }

  if (email?.trim()) {
    return email.trim();
  }

  return "Система";
}

function formatAuditDetailValue(label: string, value: string) {
  if (label !== "Тип" || !recurringTaskAuditTypes.has(value as RecurringTaskType)) {
    return value;
  }

  return getRecurringTaskTypeLabel(value as RecurringTaskType);
}

type ParsedAuditDetail = {
  label: string;
  value?: string;
  before?: string;
  after?: string;
};

function parseAuditDetails(value: string) {
  const parts = value
    .split(";")
    .map((part) => part.trim())
    .filter(Boolean);

  const changed: ParsedAuditDetail[] = [];
  const context: ParsedAuditDetail[] = [];
  const other: string[] = [];

  for (const part of parts) {
    const separatorIndex = part.indexOf(": ");
    if (separatorIndex === -1) {
      other.push(part);
      continue;
    }

    const label = part.slice(0, separatorIndex).trim();
    const content = part.slice(separatorIndex + 2).trim();
    if (!label || !content) {
      other.push(part);
      continue;
    }

    const changeSeparator = " → ";
    if (content.includes(changeSeparator)) {
      const [before, after, ...rest] = content.split(changeSeparator);
      if (before && after && rest.length === 0) {
        changed.push({ label, before, after });
        continue;
      }
    }

    context.push({ label, value: content });
  }

  return {
    changed,
    context,
    other,
    isStructured: changed.length > 0 || context.length > 0,
  };
}

export function AuditPage() {
  const controller = useAuditPageController();

  if (!controller.canViewAudit) {
    return <AccessDeniedNotice message="Доступ к журналу действий есть только у суперпользователя." />;
  }

  return (
    <PageLayout title="Аудит действий" description="Журналирует ключевые изменения в системе и действия пользователей.">
      <div data-onboarding-id="audit-page-content">
        <ListFilters>
          <div className={filterFieldWideClassName}>
            <Typography.Text type="secondary">Поиск по пользователю, действию или деталям</Typography.Text>
            <Input.Search
              allowClear
              value={controller.search}
              placeholder="Например: платеж, Иванова, вход, цена"
              onSearch={controller.handleSearch}
              onChange={(event) => {
                if (!event.target.value) {
                  controller.handleSearch("");
                }
              }}
            />
          </div>
        </ListFilters>
        <ListTable
          rowKey="id"
          emptyText={<ActionableEmptyState description="Действий по выбранным условиям не найдено" />}
          loading={controller.query.isLoading}
          queryStatus={{
            error: controller.query.error,
            isFetching: controller.query.isFetching,
            onRetry: () => {
              void controller.query.refetch();
            },
          }}
          dataSource={controller.query.data?.items}
          pagination={{ current: controller.page, pageSize: 20, total: controller.query.data?.page.total, onChange: controller.setPage }}
          columns={[
            { title: "Когда", dataIndex: "createdAtUtc", width: 170, render: (value: string) => formatDateTime(value) },
            {
              title: "Кто",
              width: 180,
              render: (_, row) => (
                <div className={styles.activityCell}>
                  <Typography.Text strong>{formatActorLabel(row.actorDisplayName, row.actorEmail)}</Typography.Text>
                </div>
              ),
            },
            {
              title: "Категория",
              dataIndex: "categoryLabel",
              width: 160,
            },
            { title: "Действие", dataIndex: "actionLabel", width: 160 },
            {
              title: "Детали",
              dataIndex: "details",
              width: 260,
              render: (value?: string | null) =>
                value ? <AuditDetails value={value} /> : <Typography.Text type="secondary">Нет данных</Typography.Text>,
            },
          ]}
        />
      </div>
    </PageLayout>
  );
}

function AuditDetails({ value }: { value: string }) {
  const parsed = parseAuditDetails(value);

  if (!parsed.isStructured) {
    return <div className={styles.activityDetails}>{value}</div>;
  }

  return (
    <div className={styles.activityDetails}>
      {parsed.changed.length > 0 ? (
        <section className={styles.activityDetailsSection}>
          <Typography.Text type="secondary" className={styles.activityDetailsHeading}>
            Изменено
          </Typography.Text>
          <ul className={styles.activityDetailsList}>
            {parsed.changed.map((item, index) => (
              <li key={`${item.label}:changed:${String(index)}`} className={styles.activityDetailsItem}>
                <Typography.Text strong>{item.label}:</Typography.Text>{" "}
                <span className={styles.activityDetailsBefore}>{formatAuditDetailValue(item.label, item.before ?? "")}</span>
                <span className={styles.activityDetailsArrow}>→</span>
                <span>{formatAuditDetailValue(item.label, item.after ?? "")}</span>
              </li>
            ))}
          </ul>
        </section>
      ) : null}
      {parsed.context.length > 0 ? (
        <section className={styles.activityDetailsSection}>
          <Typography.Text type="secondary" className={styles.activityDetailsHeading}>
            Контекст
          </Typography.Text>
          <ul className={styles.activityDetailsList}>
            {parsed.context.map((item, index) => (
              <li key={`${item.label}:context:${String(index)}`} className={styles.activityDetailsItem}>
                <Typography.Text strong>{item.label}:</Typography.Text> <span>{formatAuditDetailValue(item.label, item.value ?? "")}</span>
              </li>
            ))}
          </ul>
        </section>
      ) : null}
      {parsed.other.length > 0 ? (
        <section className={styles.activityDetailsSection}>
          <Typography.Text type="secondary" className={styles.activityDetailsHeading}>
            Дополнительно
          </Typography.Text>
          <ul className={styles.activityDetailsList}>
            {parsed.other.map((item) => (
              <li key={item} className={styles.activityDetailsItem}>
                {item}
              </li>
            ))}
          </ul>
        </section>
      ) : null}
    </div>
  );
}
