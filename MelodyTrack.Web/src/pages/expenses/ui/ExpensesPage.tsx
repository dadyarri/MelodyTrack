import { Button, DatePicker, Form, Input, InputNumber, Space, Typography } from "antd";

import { ExpenseCategorySelect } from "@/entities/reference-book";
import { DATE_FORMAT, formatDate } from "@/shared/lib";
import { formatMoney } from "@/shared/lib";
import { ActionableEmptyState } from "@/shared/ui";
import { DraftFormModal, ListFilters, ListPageScaffold, ListTable, PageLayout, ShortcutButton } from "@/shared/ui";
import { MoneyListSummaryCards } from "@/shared/ui";
import { filterFieldClassName, filterFieldWideClassName } from "@/shared/ui/filterFieldStyles";
import { DeleteOutlined, DownloadOutlined, EditOutlined, PlusOutlined } from "@/shared/ui/icons";
import { ReferenceBookCreateModal } from "@/shared/ui/ReferenceBookCreateModal";

import { useExpensesPageController } from "../model/useExpensesPageController";

export function ExpensesPage() {
  const controller = useExpensesPageController();

  return (
    <PageLayout
      title="Расходы"
      actions={
        <Space data-onboarding-id="expenses-actions">
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
            onClick={() => {
              controller.setOpen(true);
            }}
          />
        </Space>
      }
    >
      <ListPageScaffold
        contentOnboardingId="expenses-page-content"
        filtersOnboardingId="expenses-filters"
        summaryOnboardingId="expenses-summary"
        filters={
          <ListFilters>
            <div className={filterFieldWideClassName}>
              <Typography.Text type="secondary">Поиск по описанию расхода</Typography.Text>
              <Input.Search
                allowClear
                value={controller.search}
                placeholder="Введите часть описания или название статьи"
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
            lastItemAtLabel={formatOptionalDate(controller.query.data?.summary.lastItemAtUtc)}
            itemsTitle="Расходов найдено"
            lastItemTitle="Последний расход"
          />
        }
        table={
          <ListTable
            rowKey="id"
            emptyText={
              <ActionableEmptyState
                description="Расходов по выбранным условиям пока нет"
                actionLabel="Добавить расход"
                onAction={() => {
                  controller.setOpen(true);
                }}
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
            dataSource={controller.query.data?.data}
            pagination={{
              current: controller.page,
              pageSize: 10,
              total: controller.query.data?.info.total,
              onChange: controller.setPage,
            }}
            columns={[
              {
                title: "Дата",
                dataIndex: "date",
                responsive: ["sm"],
                render: (value: string) => formatDate(value),
              },
              { title: "Описание", dataIndex: "description" },
              {
                title: "Категория",
                dataIndex: "categoryName",
                responsive: ["md"],
                render: (value?: string | null) => value || "Без категории",
              },
              {
                title: "Сумма",
                dataIndex: "amount",
                render: (value: number) => formatMoney(value),
              },
              {
                title: "",
                width: controller.canEditExpenses ? 120 : 72,
                render: (_, row) => (
                  <Space size={4}>
                    {controller.canEditExpenses ? (
                      <Button
                        icon={<EditOutlined />}
                        aria-label="Редактировать расход"
                        title="Редактировать"
                        onClick={() => {
                          controller.openEdit(row);
                        }}
                      />
                    ) : null}
                    <Button
                      danger
                      icon={<DeleteOutlined />}
                      aria-label="Удалить расход"
                      title="Удалить"
                      loading={controller.deleteMutation.isPending && controller.deleteMutation.variables.id === row.id}
                      onClick={() =>
                        controller.modal.confirm({
                          title: "Удалить расход?",
                          onOk: () => {
                            controller.deleteMutation.mutate({
                              id: row.id,
                              expectedActivityId: row.lastActivity?.id,
                            });
                          },
                        })
                      }
                    />
                  </Space>
                ),
              },
            ]}
          />
        }
      />
      <DraftFormModal
        open={controller.isOpen}
        title="Новый расход"
        restored={controller.isCreateDraftRestored && controller.isOpen}
        saveStatus={controller.createDraftSaveStatus}
        showClearDraft={controller.hasCreateDraft}
        onClearDraft={controller.handleClearCreateDraft}
        onRetryDraft={controller.createDraftRetry}
        onCancel={() => {
          controller.setOpen(false);
        }}
        onOk={() => {
          controller.form.submit();
        }}
        confirmLoading={controller.createMutation.isPending}
      >
        <Form
          form={controller.form}
          layout="vertical"
          requiredMark={false}
          onFinish={controller.onCreateSubmit}
          onValuesChange={controller.onCreateValuesChange}
        >
          <Form.Item label="Категория">
            <Space.Compact className="wide">
              <Form.Item name="categoryId" noStyle>
                <ExpenseCategorySelect extraOptions={controller.createdCategoryOptions} />
              </Form.Item>
              <Button
                onClick={() => {
                  controller.setCategoryCreateOpen(true);
                }}
              >
                Новая категория
              </Button>
            </Space.Compact>
          </Form.Item>
          <Form.Item name="description" label="Описание" rules={[{ required: true }]}>
            <Input />
          </Form.Item>
          <Form.Item name="amount" label="Сумма" rules={[{ required: true }]}>
            <InputNumber min={0.01} precision={2} step={0.01} className="wide" />
          </Form.Item>
          <Form.Item name="date" label="Дата" rules={[{ required: true }]}>
            <DatePicker format={DATE_FORMAT} className="wide" />
          </Form.Item>
        </Form>
      </DraftFormModal>
      <DraftFormModal
        open={controller.editingExpense !== null}
        title="Редактировать расход"
        restored={controller.editDraft.restored}
        saveStatus={controller.editDraft.status}
        showClearDraft={controller.editDraft.hasDraft}
        onClearDraft={() => {
          void controller.editDraft.discard().then(() => {
            controller.editForm.resetFields();
          });
        }}
        stale={controller.editDraft.isStale}
        onReapplyDraft={controller.editDraft.reapply}
        onRetryDraft={controller.editDraft.retry}
        onCancel={controller.closeEdit}
        onOk={() => {
          controller.editForm.submit();
        }}
        confirmLoading={controller.editMutation.isPending}
      >
        <Form
          form={controller.editForm}
          layout="vertical"
          requiredMark={false}
          onFinish={controller.onEditSubmit}
          onValuesChange={controller.onEditValuesChange}
        >
          <Form.Item name="categoryId" label="Категория">
            <ExpenseCategorySelect extraOptions={controller.createdCategoryOptions} />
          </Form.Item>
          <Form.Item name="description" label="Описание" rules={[{ required: true }]}>
            <Input />
          </Form.Item>
          <Form.Item name="amount" label="Сумма" rules={[{ required: true }]}>
            <InputNumber min={0.01} precision={2} step={0.01} className="wide" />
          </Form.Item>
          <Form.Item name="date" label="Дата" rules={[{ required: true }]}>
            <DatePicker format={DATE_FORMAT} className="wide" />
          </Form.Item>
        </Form>
      </DraftFormModal>
      <ReferenceBookCreateModal
        open={controller.isCategoryCreateOpen}
        title="Новая категория расхода"
        draftKey="draft:expense-categories:create"
        confirmLoading={controller.createCategoryMutation.isPending}
        onCancel={() => {
          controller.setCategoryCreateOpen(false);
        }}
        onSubmit={(values, clearAfterSuccess) => {
          controller.createCategoryMutation.mutate(values, { onSuccess: () => void clearAfterSuccess() });
        }}
      />
    </PageLayout>
  );
}

function formatOptionalDate(value?: string | null) {
  return value ? formatDate(value) : "Нет данных";
}
