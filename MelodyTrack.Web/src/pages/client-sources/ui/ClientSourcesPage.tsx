import { Button } from "antd";

import { clientQueryKeys } from "@/entities/client";
import { clientSourcesApi } from "@/entities/reference-book";
import { useReferenceBookPageController } from "@/features/manage-reference-book";
import { ActionableEmptyState } from "@/shared/ui";
import { ListPageScaffold, ListTable, PageLayout, ShortcutButton } from "@/shared/ui";
import { DeleteOutlined, PlusOutlined } from "@/shared/ui/icons";
import { ReferenceBookCreateModal } from "@/shared/ui/ReferenceBookCreateModal";

export function ClientSourcesPage() {
  const controller = useReferenceBookPageController({
    successMessages: {
      create: "Источник создан",
      delete: "Источник удален",
    },
    staleConflict: {
      title: "Источник уже изменен",
      okText: "Удалить все равно",
      cancelText: "Обновить список",
    },
    createItem: (values) => clientSourcesApi.create(values),
    deleteItem: (id, options) => clientSourcesApi.remove(id, options),
    listQueryKey: clientQueryKeys.sources,
    listQueryFn: () => clientSourcesApi.list(),
    invalidateQueryKeys: [clientQueryKeys.all],
  });

  return (
    <PageLayout
      title="Источники клиентов"
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
        contentOnboardingId="client-sources-page-content"
        table={
          <ListTable
            rowKey="id"
            emptyText={
              <ActionableEmptyState
                description="Источники клиентов пока не созданы"
                actionLabel="Добавить источник"
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
                    aria-label="Удалить источник клиентов"
                    title="Удалить"
                    loading={controller.deleteMutation.isPending && controller.deleteMutation.variables.id === row.id}
                    onClick={() => {
                      controller.modal.confirm({
                        title: "Удалить источник?",
                        content: "Связанные клиенты сохранятся без источника.",
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
        title="Новый источник клиента"
        draftKey="draft:client-sources:create"
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
