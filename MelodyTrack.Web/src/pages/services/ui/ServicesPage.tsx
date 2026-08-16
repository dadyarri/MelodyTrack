import { App as AntdApp, Button, Form, Input, InputNumber, Space, Switch, Tag } from "antd";

import { formatMoney } from "@/shared/lib";
import { ActionableEmptyState, DraftFormModal, ListPageScaffold, ListTable, PageLayout, ShortcutButton } from "@/shared/ui";
import { DeleteOutlined, DollarOutlined, EditOutlined, PlusOutlined } from "@/shared/ui/icons";

import { useServicesPageController } from "../model/useServicesPageController";

export function ServicesPage() {
  const controller = useServicesPageController();
  const { modal } = AntdApp.useApp();

  return (
    <PageLayout
      title="Услуги"
      actions={
        controller.canManageServices ? (
          <ShortcutButton
            data-onboarding-id="services-actions"
            shortcut="A"
            type="primary"
            leadingIcon={<PlusOutlined />}
            label="Добавить"
            onClick={() => {
              controller.setCreateOpen(true);
            }}
          />
        ) : undefined
      }
    >
      <ListPageScaffold
        contentOnboardingId="services-page-content"
        table={
          <ListTable
            rowKey="id"
            emptyText={
              <ActionableEmptyState
                description="Услуги пока не созданы"
                actionLabel={controller.canManageServices ? "Добавить услугу" : undefined}
                onAction={
                  controller.canManageServices
                    ? () => {
                        controller.setCreateOpen(true);
                      }
                    : undefined
                }
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
              { title: "Название", dataIndex: "name" },
              {
                title: "Название для клиента",
                dataIndex: "publicName",
                responsive: ["lg"],
                render: (value?: string | null) => value || "—",
              },
              {
                title: "Тип",
                width: 132,
                responsive: ["md"],
                render: (_, row) => (row.isConsultation ? <Tag color="blue">Консультация</Tag> : null),
              },
              { title: "Описание", dataIndex: "description", responsive: ["lg"] },
              {
                title: "Цена",
                dataIndex: "price",
                render: (value: number) => formatMoney(value),
              },
              {
                title: "",
                width: 132,
                render: (_, row) => (
                  <Space>
                    <Button
                      icon={<EditOutlined />}
                      aria-label="Редактировать услугу"
                      title="Редактировать"
                      disabled={!controller.canManageServices}
                      onClick={() => {
                        if (!controller.canManageServices) {
                          return;
                        }

                        controller.setEditing(row);
                      }}
                    />
                    <Button
                      icon={<DollarOutlined />}
                      aria-label="Обновить цену услуги"
                      title="Обновить цену"
                      disabled={!controller.canManageServices}
                      onClick={() => {
                        if (!controller.canManageServices) {
                          return;
                        }

                        controller.setPricing(row);
                        controller.priceForm.setFieldValue("price", row.price);
                      }}
                    />
                    <Button
                      danger
                      icon={<DeleteOutlined />}
                      aria-label="Удалить услугу"
                      title="Удалить"
                      loading={controller.deleteMutation.isPending && controller.deleteMutation.variables === row.id}
                      disabled={!controller.canManageServices}
                      onClick={() => {
                        if (!controller.canManageServices) {
                          return;
                        }

                        modal.confirm({
                          title: "Удалить услугу?",
                          content: "Услуга будет удалена только если она еще не использовалась в расписании или платежах.",
                          onOk: () => {
                            controller.deleteMutation.mutate(row.id);
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
      <DraftFormModal
        open={controller.canManageServices && controller.isCreateOpen}
        title="Новая услуга"
        restored={controller.isCreateDraftRestored && controller.isCreateOpen}
        saveStatus={controller.createDraftSaveStatus}
        showClearDraft={controller.hasCreateDraft}
        onClearDraft={controller.handleClearCreateDraft}
        onRetryDraft={controller.createDraftRetry}
        onCancel={() => {
          controller.setCreateOpen(false);
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
          <Form.Item name="name" label="Название" rules={[{ required: true }]}>
            <Input />
          </Form.Item>
          <Form.Item
            name="publicName"
            label="Название для клиента"
            extra="Будет показано в клиентском календаре. Если не указано, используется внутреннее название."
          >
            <Input />
          </Form.Item>
          <Form.Item name="description" label="Описание">
            <Input />
          </Form.Item>
          <Form.Item name="isConsultation" label="Консультация" valuePropName="checked" initialValue={false}>
            <Switch />
          </Form.Item>
          <Form.Item name="price" label="Цена" rules={[{ required: true }]}>
            <InputNumber min={0} className="wide" />
          </Form.Item>
        </Form>
      </DraftFormModal>
      <DraftFormModal
        open={controller.canManageServices && Boolean(controller.editing)}
        title="Редактировать услугу"
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
        onCancel={() => {
          controller.setEditing(null);
        }}
        onOk={() => {
          controller.editForm.submit();
        }}
        confirmLoading={controller.updateMutation.isPending}
      >
        <Form
          form={controller.editForm}
          layout="vertical"
          requiredMark={false}
          onFinish={controller.onEditSubmit}
          onValuesChange={controller.onEditValuesChange}
        >
          <Form.Item name="name" label="Название" rules={[{ required: true }]}>
            <Input />
          </Form.Item>
          <Form.Item name="publicName" label="Название для клиента" extra="Будет показано в клиентском календаре.">
            <Input />
          </Form.Item>
          <Form.Item name="description" label="Описание">
            <Input />
          </Form.Item>
          <Form.Item name="isConsultation" label="Консультация" valuePropName="checked">
            <Switch />
          </Form.Item>
        </Form>
      </DraftFormModal>
      <DraftFormModal
        open={controller.canManageServices && Boolean(controller.pricing)}
        title="Обновить цену"
        restored={controller.priceDraft.restored}
        saveStatus={controller.priceDraft.status}
        showClearDraft={controller.priceDraft.hasDraft}
        onClearDraft={() => {
          void controller.priceDraft.discard().then(() => {
            controller.priceForm.resetFields();
          });
        }}
        stale={controller.priceDraft.isStale}
        onReapplyDraft={controller.priceDraft.reapply}
        onRetryDraft={controller.priceDraft.retry}
        onCancel={() => {
          controller.setPricing(null);
        }}
        onOk={() => {
          controller.priceForm.submit();
        }}
        confirmLoading={controller.priceMutation.isPending}
      >
        <Form
          form={controller.priceForm}
          layout="vertical"
          onFinish={controller.onPriceSubmit}
          onValuesChange={controller.onPriceValuesChange}
        >
          <Form.Item name="price" label="Цена" rules={[{ required: true }]}>
            <InputNumber min={0} className="wide" />
          </Form.Item>
        </Form>
      </DraftFormModal>
    </PageLayout>
  );
}
