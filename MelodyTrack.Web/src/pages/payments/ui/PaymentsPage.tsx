import { App as AntdApp, Button, DatePicker, Input, Space, Typography } from "antd";

import { ClientSelect } from "@/entities/client";
import { ServiceSelect } from "@/entities/service";
import { ClientQuickCreateModal } from "@/features/manage-client";
import { PaymentCreateModal } from "@/features/record-payment";
import { DATE_FORMAT, formatDateTime } from "@/shared/lib";
import { formatMoney } from "@/shared/lib";
import { ActionableEmptyState, MoneyListSummaryCards } from "@/shared/ui";
import { ListFilters, ListPageScaffold, ListTable, PageLayout, ShortcutButton } from "@/shared/ui";
import { filterFieldClassName, filterFieldServiceClassName, filterFieldWideClassName } from "@/shared/ui/filterFieldStyles";
import { DeleteOutlined, DownloadOutlined, EditOutlined, PlusOutlined } from "@/shared/ui/icons";

import { formatOptionalDateTime, usePaymentsPageController } from "../model/usePaymentsPageController";

export function PaymentsPage() {
  const controller = usePaymentsPageController();
  const { modal } = AntdApp.useApp();

  return (
    <PageLayout
      title="Платежи"
      actions={
        <Space data-onboarding-id="payments-actions">
          <ShortcutButton
            shortcut="X"
            leadingIcon={<DownloadOutlined />}
            loading={controller.exportMutation.isPending}
            label="Экспорт"
            onClick={() => {
              controller.exportMutation.mutate();
            }}
          />
          <ShortcutButton
            shortcut="A"
            type="primary"
            leadingIcon={<PlusOutlined />}
            label="Добавить"
            onClick={controller.openCreateModal}
          />
        </Space>
      }
    >
      <ListPageScaffold
        contentOnboardingId="payments-page-content"
        filtersOnboardingId="payments-filters"
        summaryOnboardingId="payments-summary"
        filters={
          <ListFilters>
            <div className={filterFieldWideClassName}>
              <Typography.Text type="secondary">Поиск по клиенту, услуге или описанию</Typography.Text>
              <Input.Search
                allowClear
                value={controller.search}
                placeholder="Введите имя клиента, услугу или текст описания"
                onSearch={(value) => {
                  controller.setSearch(value);
                }}
                onChange={(event) => {
                  if (!event.target.value) {
                    controller.setSearch("");
                  }
                }}
              />
            </div>
            <div className={filterFieldClassName}>
              <Typography.Text type="secondary">Клиент</Typography.Text>
              <ClientSelect
                value={controller.clientId}
                onChange={(value) => {
                  controller.setClientId(value);
                }}
              />
            </div>
            <div className={filterFieldServiceClassName}>
              <Typography.Text type="secondary">Услуга</Typography.Text>
              <ServiceSelect
                showPrice
                value={controller.serviceId}
                onChange={(value) => {
                  controller.setServiceId(value);
                }}
              />
            </div>
            <div className={filterFieldClassName}>
              <Typography.Text type="secondary">Период</Typography.Text>
              <DatePicker.RangePicker
                value={controller.dateRange}
                format={DATE_FORMAT}
                onChange={(value) => {
                  controller.setDateRange(value);
                }}
              />
            </div>
            <div className={filterFieldClassName}>
              <Typography.Text type="secondary">Действия</Typography.Text>
              <Button onClick={controller.resetFilters}>Сбросить</Button>
            </div>
          </ListFilters>
        }
        summary={
          <MoneyListSummaryCards
            totalAmount={controller.query.data?.summary.totalAmount}
            itemsCount={controller.query.data?.summary.itemsCount}
            lastItemAtLabel={formatOptionalDateTime(controller.query.data?.summary.lastItemAtUtc)}
            itemsTitle="Платежей найдено"
            lastItemTitle="Последний платеж"
          />
        }
        table={
          <ListTable
            rowKey="id"
            emptyText={
              <ActionableEmptyState
                description="Платежей по выбранным условиям пока нет"
                actionLabel="Добавить платёж"
                onAction={controller.openCreateModal}
              />
            }
            loading={controller.query.isLoading}
            queryStatus={{
              isError: controller.query.isError,
              isFetching: controller.query.isFetching,
              onRetry: () => {
                void controller.query.refetch();
              },
            }}
            dataSource={controller.query.data?.items}
            scroll={{ x: "max-content" }}
            pagination={{
              current: controller.page,
              pageSize: 10,
              total: controller.query.data?.page.total,
              onChange: controller.setPage,
            }}
            columns={[
              {
                title: "Дата",
                dataIndex: "date",
                responsive: ["sm"],
                render: (value: string) => formatDateTime(value),
              },
              {
                title: "Клиент",
                render: (_, row) => `${row.client.lastName} ${row.client.firstName}`,
              },
              { title: "Услуга", responsive: ["md"], render: (_, row) => row.service?.name },
              {
                title: "Сумма",
                dataIndex: "amount",
                render: (value: number) => formatMoney(value),
              },
              {
                title: "Описание",
                dataIndex: "description",
                responsive: ["lg"],
                render: (value?: string | null) => value?.trim() || "Без описания",
              },
              {
                title: "",
                width: 112,
                render: (_, row) => (
                  <Space>
                    <Button
                      icon={<EditOutlined />}
                      aria-label="Редактировать платёж"
                      title="Редактировать"
                      onClick={() => {
                        controller.openEditModal(row);
                      }}
                    />
                    <Button
                      danger
                      icon={<DeleteOutlined />}
                      aria-label="Удалить платёж"
                      title="Удалить"
                      loading={controller.deleteMutation.isPending && controller.deleteMutation.variables.id === row.id}
                      onClick={() => {
                        modal.confirm({
                          title: "Удалить платеж?",
                          onOk: () => {
                            controller.deleteMutation.mutate({
                              id: row.id,
                              expectedActivityId: row.lastActivity?.id,
                            });
                          },
                        });
                      }}
                    />
                  </Space>
                ),
              },
            ]}
          />
        }
      />
      <PaymentCreateModal
        open={controller.isCreateModalOpen}
        editing={Boolean(controller.editingPayment)}
        hasDraft={controller.hasCreateDraft}
        draftRestored={controller.isCreateDraftRestored && controller.isCreateModalOpen}
        draftSaveStatus={controller.createDraftSaveStatus}
        form={controller.form}
        createPending={controller.saveMutation.isPending}
        createdClientOptions={controller.createdClientOptions}
        draftHydrationRef={controller.draftHydrationRef}
        selectedCreateServiceId={controller.selectedCreateServiceId}
        selectedServicePrice={controller.selectedServicePrice}
        onCancel={controller.closeCreateModal}
        onClearDraft={controller.handleClearCreateDraft}
        draftStale={controller.activeDraft.isStale}
        onReapplyDraft={controller.activeDraft.reapply}
        onRetryDraft={controller.activeDraft.retry}
        onSubmit={(values) => {
          controller.saveMutation.mutate({
            values,
            expectedActivityId: controller.editingBaselineActivityId ?? undefined,
          });
        }}
        onValuesChange={controller.onCreateValuesChange}
        onCreateClient={() => {
          controller.setQuickClientCreateOpen(true);
        }}
        onClientLabelChange={controller.setCreateClientLabel}
        onServiceLabelChange={controller.setCreateServiceLabel}
        onServicePriceChange={controller.setSelectedServicePrice}
      />
      <ClientQuickCreateModal
        open={controller.isQuickClientCreateOpen}
        onCancel={() => {
          controller.setQuickClientCreateOpen(false);
        }}
        onCreated={controller.onQuickClientCreated}
      />
    </PageLayout>
  );
}
