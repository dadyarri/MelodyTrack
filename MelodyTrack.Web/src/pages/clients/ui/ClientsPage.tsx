import { Button, Input, Popconfirm, Space, Tag } from "antd";

import { type Client, type ClientLifecycleStatus, formatClientName } from "@/entities/client";
import { CourseEnrollmentCreateModal } from "@/features/enroll-client-course";
import { ClientEditorModal, ClientVacationsModal } from "@/features/manage-client";
import { formatDateTime } from "@/shared/lib";
import { formatMoney } from "@/shared/lib";
import { ActionableEmptyState } from "@/shared/ui";
import { ListFilters, ListPageScaffold, ListTable, PageLayout, ShortcutButton, UrlCopyModal } from "@/shared/ui";
import { filterFieldWideClassName } from "@/shared/ui/filterFieldStyles";
import { CloseOutlined, DeleteOutlined, EditOutlined, PlusOutlined, ProfileOutlined, ReloadOutlined } from "@/shared/ui/icons";
import { ReferenceBookCreateModal } from "@/shared/ui/ReferenceBookCreateModal";
import tableLinkButtonStyles from "@/shared/ui/TableLinkButton.module.css";
import { ClientHistoryDrawer } from "@/widgets/client-history";

import { useClientsPageController } from "../model/useClientsPageController";

export function ClientsPage() {
  const controller = useClientsPageController();

  return (
    <PageLayout
      title="Клиенты"
      actions={
        controller.canCreateClients ? (
          <ShortcutButton
            data-onboarding-id="clients-actions"
            shortcut="A"
            type="primary"
            leadingIcon={<PlusOutlined />}
            label="Добавить"
            onClick={() => {
              controller.openEditor();
            }}
          />
        ) : undefined
      }
    >
      <ListPageScaffold
        contentOnboardingId="clients-page-content"
        filters={
          <ListFilters>
            <div className={filterFieldWideClassName}>
              <Input.Search
                allowClear
                value={controller.search}
                placeholder="Поиск по ФИО"
                onSearch={controller.handleSearch}
                onChange={(event) => {
                  if (!event.target.value) {
                    controller.handleSearch("");
                  }
                }}
              />
            </div>
          </ListFilters>
        }
        table={
          <ListTable
            rowKey="id"
            emptyText={
              <ActionableEmptyState
                description="Клиентов по выбранным условиям пока нет"
                actionLabel={controller.canCreateClients ? "Добавить клиента" : undefined}
                onAction={controller.canCreateClients ? controller.openEditor : undefined}
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
            dataSource={controller.clients}
            pagination={{
              current: controller.pagination.current,
              pageSize: controller.pagination.pageSize,
              total: controller.pagination.total,
              onChange: controller.setPage,
            }}
            columns={[
              {
                title: "ФИО",
                render: (_, row) => (
                  <Button
                    type="link"
                    className={tableLinkButtonStyles.button}
                    onClick={() => {
                      controller.setHistoryClient(row);
                    }}
                  >
                    {formatClientName(row)}
                  </Button>
                ),
              },
              {
                title: "Статус",
                width: 118,
                responsive: ["sm"],
                render: (_, row) => <ClientLifecycleTag client={row} />,
              },
              {
                title: "Последняя запись",
                responsive: ["lg"],
                render: (_, row) => (row.lastAppointmentAtUtc ? formatDateTime(row.lastAppointmentAtUtc) : "Нет"),
              },
              {
                title: "Следующая запись",
                responsive: ["md"],
                render: (_, row) => (row.nextAppointmentAtUtc ? formatDateTime(row.nextAppointmentAtUtc) : "Нет"),
              },
              {
                title: "Баланс",
                dataIndex: "balance",
                responsive: ["sm"],
                render: (_, row) => <Tag color={row.balance < 0 ? "red" : "green"}>{formatMoney(row.balance)}</Tag>,
              },
              {
                title: "",
                width: 154,
                render: (_, row) => (
                  <Space>
                    <Button
                      icon={<ProfileOutlined />}
                      aria-label="Открыть карточку клиента"
                      title="Открыть карточку"
                      onClick={() => {
                        controller.setHistoryClient(row);
                      }}
                    />
                    {controller.canCreateClients && (row.lifecycleStatus === 1 || row.lifecycleStatus === 2) ? (
                      <Popconfirm
                        title="Закрыть лид?"
                        description="Лид останется в базе, но будет исключен из активной воронки."
                        okText="Закрыть"
                        cancelText="Отмена"
                        okButtonProps={{ danger: true, loading: controller.leadStatusMutation.isPending }}
                        onConfirm={() => {
                          controller.setLeadClosed(row, true);
                        }}
                      >
                        <Button
                          danger
                          icon={<CloseOutlined />}
                          aria-label="Закрыть лид"
                          title="Закрыть лид"
                          loading={controller.leadStatusMutation.isPending && controller.leadStatusMutation.variables.id === row.id}
                        />
                      </Popconfirm>
                    ) : null}
                    {controller.canCreateClients && row.lifecycleStatus === 3 ? (
                      <Popconfirm
                        title="Вернуть лид в работу?"
                        description="Статус будет пересчитан по записям клиента."
                        okText="Вернуть"
                        cancelText="Отмена"
                        okButtonProps={{ loading: controller.leadStatusMutation.isPending }}
                        onConfirm={() => {
                          controller.setLeadClosed(row, false);
                        }}
                      >
                        <Button
                          icon={<ReloadOutlined />}
                          aria-label="Вернуть лид в работу"
                          title="Вернуть лид в работу"
                          loading={controller.leadStatusMutation.isPending && controller.leadStatusMutation.variables.id === row.id}
                        />
                      </Popconfirm>
                    ) : null}
                    <Button
                      icon={<EditOutlined />}
                      aria-label="Редактировать клиента"
                      title="Редактировать"
                      onClick={() => {
                        controller.openEditor(row);
                      }}
                    />
                    <Button
                      danger
                      icon={<DeleteOutlined />}
                      aria-label="Удалить клиента"
                      title="Удалить"
                      loading={controller.deleteMutation.isPending && controller.deleteMutation.variables.id === row.id}
                      onClick={() => {
                        controller.confirmDelete(row);
                      }}
                    />
                  </Space>
                ),
              },
            ]}
          />
        }
      />
      <ClientEditorModal
        open={controller.isCreateOpen}
        editing={Boolean(controller.editing)}
        hasDraft={controller.hasCreateDraft}
        draftRestored={controller.isCreateDraftRestored && controller.isCreateOpen}
        draftSaveStatus={controller.createDraftSaveStatus}
        form={controller.form}
        savePending={controller.saveMutation.isPending}
        isStale={controller.isEditingClientStale}
        staleActivity={controller.currentEditingClient?.lastActivity}
        sourceOptions={controller.createdSourceOptions}
        onCancel={controller.closeEditor}
        onClearDraft={controller.handleClearCreateDraft}
        onSubmit={controller.onSubmit}
        onValuesChange={controller.onValuesChange}
        onCreateSource={controller.canCreateClients ? controller.openSourceCreate : undefined}
        onSourceLabelChange={controller.onSourceLabelChange}
        draftStale={controller.editorDraft.isStale}
        onReapplyDraft={controller.editorDraft.reapply}
        onRetryDraft={controller.editorDraft.retry}
      />
      <ReferenceBookCreateModal
        open={controller.isSourceCreateOpen}
        title="Новый источник клиента"
        draftKey="draft:client-sources:create"
        confirmLoading={controller.createSourceMutation.isPending}
        onCancel={controller.closeSourceCreate}
        onSubmit={(values, clearAfterSuccess) => {
          controller.createSourceMutation.mutate(values, { onSuccess: () => void clearAfterSuccess() });
        }}
      />
      <ClientHistoryDrawer
        client={controller.historyClient}
        data={controller.historyQuery.data}
        isLoading={controller.historyQuery.isLoading}
        error={controller.historyQuery.error}
        courseEnrollments={controller.courseEnrollmentsQuery.data}
        isCourseEnrollmentsLoading={controller.courseEnrollmentsQuery.isLoading}
        isCourseEnrollmentsError={controller.courseEnrollmentsQuery.isError}
        onClose={controller.closeHistoryClient}
        onCreateAppointment={controller.clientHistoryActions.onCreateAppointment}
        onCreatePayment={controller.clientHistoryActions.onCreatePayment}
        onCreateCourseEnrollment={controller.canCreateClients ? controller.openEnrollmentCreate : undefined}
        onCreatePortalLink={controller.canCreateClients ? controller.onCreatePortalLink : undefined}
        isCreatingPortalLink={controller.createPortalLinkMutation.isPending}
        onRevokePortalLink={controller.canCreateClients ? controller.onRevokePortalLink : undefined}
        isRevokingPortalLink={controller.revokePortalLinkMutation.isPending}
        onCreateCalendarSubscription={controller.canCreateClients ? controller.onCreateCalendarSubscription : undefined}
        isCreatingCalendarSubscription={controller.createCalendarSubscriptionMutation.isPending}
        onResetPortalPin={controller.canCreateClients ? controller.onResetPortalPin : undefined}
        isResettingPortalPin={controller.resetPortalPinMutation.isPending}
        onDeleteCourseEnrollment={controller.canCreateClients ? controller.onDeleteEnrollment : undefined}
        onOpenCourseProgress={controller.openCourseProgress}
        onUpdateThemeProgress={controller.canCreateClients ? controller.onUpdateThemeProgress : undefined}
        onEventsPageChange={controller.setHistoryEventsPage}
        onEditVacations={controller.canCreateClients ? controller.openVacationsEditor : undefined}
      />
      <CourseEnrollmentCreateModal
        open={controller.isEnrollmentCreateOpen}
        clientId={controller.historyClient?.id}
        clientName={controller.historyClient ? formatClientName(controller.historyClient) : undefined}
        options={controller.availableEnrollmentCourses}
        confirmLoading={controller.createEnrollmentMutation.isPending}
        onCancel={controller.closeEnrollmentCreate}
        onSubmit={(values, clearAfterSuccess) => {
          controller.createEnrollmentMutation.mutate(values, { onSuccess: () => void clearAfterSuccess() });
        }}
      />
      <ClientVacationsModal
        client={controller.vacationsClient}
        form={controller.vacationsForm}
        saving={controller.vacationsMutation.isPending}
        draftStatus={controller.vacationsDraft.status}
        draftRestored={controller.vacationsDraft.restored}
        hasDraft={controller.vacationsDraft.hasDraft}
        onDiscardDraft={() => {
          void controller.vacationsDraft.discard().then(() => {
            controller.vacationsForm.resetFields();
          });
        }}
        onValuesChange={controller.vacationsDraft.formProps.onValuesChange}
        draftStale={controller.vacationsDraft.isStale}
        onReapplyDraft={controller.vacationsDraft.reapply}
        onRetryDraft={controller.vacationsDraft.retry}
        onCancel={controller.closeVacationsEditor}
        onSubmit={controller.saveVacations}
      />
      <UrlCopyModal {...controller.urlModalProps} />
    </PageLayout>
  );
}

function ClientLifecycleTag({ client }: { client: Client }) {
  const lifecycleStatus = client.lifecycleStatus;
  if (lifecycleStatus === 0) {
    return null;
  }

  const labels: Record<Exclude<ClientLifecycleStatus, 0>, { label: string; color: string }> = {
    1: { label: "Лид", color: "blue" },
    2: { label: "Думает", color: "gold" },
    3: { label: "Закрыт", color: "default" },
  };
  const status = labels[lifecycleStatus];

  return <Tag color={status.color}>{status.label}</Tag>;
}
