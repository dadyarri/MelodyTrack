import { Button } from "antd";

import { expenseQueryKeys } from "@/entities/expense";
import { expenseCategoriesApi, referenceBookQueryKeys } from "@/entities/reference-book";
import { useReferenceBookPageController } from "@/features/manage-reference-book";
import { ActionableEmptyState } from "@/shared/ui";
import { ListPageScaffold, ListTable, PageLayout, ShortcutButton } from "@/shared/ui";
import { DeleteOutlined, PlusOutlined } from "@/shared/ui/icons";
import { ReferenceBookCreateModal } from "@/shared/ui/ReferenceBookCreateModal";

export function ExpenseCategoriesPage() {
  const controller = useReferenceBookPageController({
    successMessages: {
      create: "Категория создана",
      delete: "Категория удалена",
    },
    staleConflict: {
      title: "Категория уже изменена",
      okText: "Удалить все равно",
      cancelText: "Обновить список",
    },
    createItem: (values) => expenseCategoriesApi.create(values),
    deleteItem: (id, options) => expenseCategoriesApi.remove(id, options),
    listQueryKey: referenceBookQueryKeys.expenseCategories,
    listQueryFn: () => expenseCategoriesApi.list(),
    invalidateQueryKeys: [expenseQueryKeys.all],
  });

  return (
    <PageLayout
      title="Статьи расходов"
      actions={
        <ShortcutButton
          shortcut="A"
          type="primary"
          leadingIcon={<PlusOutlined />}
          label="Добавить"
          onClick={() => {
            controller.setCreateOpen(true);
          }}
        />
      }
    >
      <ListPageScaffold
        contentOnboardingId="expense-categories-page-content"
        table={
          <ListTable
            rowKey="id"
            emptyText={
              <ActionableEmptyState
                description="Статьи расходов пока не созданы"
                actionLabel="Добавить статью"
                onAction={() => {
                  controller.setCreateOpen(true);
                }}
              />
            }
            loading={controller.query.isLoading}
            queryStatus={{
              error: controller.query.error,
              isFetching: controller.query.isFetching,
              onRetry: () => {
                void controller.query.refetch();
              },
            }}
            dataSource={controller.query.data}
            pagination={false}
            columns={[
              { title: "Название", dataIndex: "name" },
              {
                title: "",
                width: 72,
                render: (_, row) => (
                  <Button
                    danger
                    icon={<DeleteOutlined />}
                    aria-label="Удалить статью расходов"
                    title="Удалить"
                    loading={controller.deleteMutation.isPending && controller.deleteMutation.variables.id === row.id}
                    onClick={() => {
                      controller.modal.confirm({
                        title: "Удалить категорию?",
                        content: "Связанные расходы сохранятся без категории.",
                        onOk: () => {
                          controller.onDelete(row.id, row.lastActivity?.id);
                        },
                      });
                    }}
                  />
                ),
              },
            ]}
          />
        }
      />
      <ReferenceBookCreateModal
        open={controller.isCreateOpen}
        title="Новая категория расхода"
        draftKey="draft:expense-categories:create"
        confirmLoading={controller.createMutation.isPending}
        onCancel={() => {
          controller.setCreateOpen(false);
        }}
        onSubmit={(values, clearAfterSuccess) => {
          controller.createMutation.mutate(values, { onSuccess: () => void clearAfterSuccess() });
        }}
      />
    </PageLayout>
  );
}
