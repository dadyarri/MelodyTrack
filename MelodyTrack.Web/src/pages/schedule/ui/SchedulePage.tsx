import { Button, Space, Typography } from "antd";
import dayjs from "dayjs";

import { UserSelect } from "@/entities/user";
import {
  AppointmentCreateModal,
  AppointmentDetailsModal,
  AppointmentEditModal,
  RecurringDeleteModal,
  RecurringRescheduleModal,
} from "@/features/manage-appointment";
import { ClientQuickCreateModal } from "@/features/manage-client";
import { VacationRangeModal } from "@/features/manage-vacation-requests";
import { PaymentCreateModal } from "@/features/record-payment";
import { PageLayout, ShortcutButton } from "@/shared/ui";
import { LeftOutlined, PlusOutlined, RightOutlined } from "@/shared/ui/icons";
import { AppointmentsCalendar } from "@/widgets/schedule-calendar";

import { useSchedulePageController } from "../model/useSchedulePageController";
import styles from "./SchedulePage.module.css";

export function SchedulePage() {
  const controller = useSchedulePageController();
  const weekRangeLabel = formatScheduleWeekRange(controller.weekStart);

  return (
    <>
      <PageLayout
        title="Расписание"
        description={weekRangeLabel}
        customClass={styles.pageShell}
        actions={
          <Space wrap className={styles.headerActions} data-onboarding-id="schedule-header-actions">
            <ShortcutButton
              shortcut="←"
              leadingIcon={<LeftOutlined />}
              label="Пред."
              onClick={() => {
                controller.setWeekStart(controller.weekStart.subtract(1, "week"));
              }}
            />
            <ShortcutButton
              shortcut="Home"
              label="Сегодня"
              onClick={() => {
                controller.setWeekStart(dayjs().startOf("week"));
              }}
            />
            <ShortcutButton
              shortcut="→"
              leadingIcon={<RightOutlined />}
              label="След."
              onClick={() => {
                controller.setWeekStart(controller.weekStart.add(1, "week"));
              }}
            />
            {controller.canCreateAppointments ? (
              <ShortcutButton
                shortcut="A"
                type="primary"
                leadingIcon={<PlusOutlined />}
                label="Добавить"
                onClick={controller.openCreateModal}
              />
            ) : null}
            {controller.canCreateVacations ? (
              <ShortcutButton
                shortcut="V"
                label="Отпуск"
                onClick={() => {
                  const start = dayjs().add(1, "hour").startOf("hour");
                  controller.openVacationModal([start, start.add(1, "hour")]);
                }}
              />
            ) : null}
          </Space>
        }
      >
        <section className={styles.pageShell}>
          {!controller.isSpecialistFilterLocked ? (
            <div className={styles.toolbar} data-onboarding-id="schedule-toolbar">
              <div className={styles.quickFilters}>
                <Typography.Text type="secondary">Преподаватель</Typography.Text>
                <Space.Compact className={styles.quickFiltersControls}>
                  <UserSelect value={controller.effectiveProviderFilterId} onChange={controller.setProviderFilterId} />
                  {controller.auth.user?.id
                    ? (() => {
                        const currentUserId = controller.auth.user.id;
                        return (
                          <ShortcutButton
                            shortcut="M"
                            type={controller.effectiveProviderFilterId === currentUserId ? "primary" : "default"}
                            disabled={false}
                            label="Моё"
                            onClick={() => {
                              controller.setProviderFilterId(controller.providerFilterId === currentUserId ? undefined : currentUserId);
                            }}
                          />
                        );
                      })()
                    : null}
                  <Button
                    disabled={!controller.effectiveProviderFilterId}
                    onClick={() => {
                      controller.setProviderFilterId(undefined);
                    }}
                  >
                    Сбросить
                  </Button>
                </Space.Compact>
              </div>
            </div>
          ) : null}
          <div className={styles.calendar} data-onboarding-id="schedule-calendar">
            <AppointmentsCalendar
              appointments={controller.filteredAppointments}
              availability={controller.providerAvailabilityQuery.data}
              canCreateAppointments={controller.canCreateAppointments}
              canCreateVacations={controller.canCreateVacations}
              loading={controller.query.isLoading}
              onReschedule={(appointment, startDate) => {
                if (appointment.recurringRule) {
                  controller.setAppointmentToReschedule(appointment);
                  controller.setAppointmentToRescheduleStartDate(startDate);
                  controller.setAppointmentToRescheduleBaselineActivityId(appointment.lastActivity?.id ?? null);
                  return;
                }

                controller.modal.confirm({
                  title: "Перенести запись?",
                  content: `Перенести запись на ${startDate.format("DD.MM.YYYY HH:mm")}?`,
                  onOk: () => {
                    controller.rescheduleMutation.mutate({
                      appointment,
                      startDate,
                      expectedActivityId: appointment.lastActivity?.id,
                    });
                  },
                });
              }}
              range={[controller.weekStart, controller.weekStart.endOf("week")]}
              visibleHours={controller.visibleHours}
              reschedulePendingAppointmentId={
                controller.rescheduleMutation.isPending ? controller.rescheduleMutation.variables.appointment.id : null
              }
              onCreateAt={controller.openCreateModalAt}
              onCreateVacation={(startDate, endDate) => {
                controller.openVacationModal([startDate, endDate]);
              }}
              onEditVacation={controller.canManageVacations ? controller.openVacationEditor : undefined}
              onSelect={(appointment) => {
                controller.setSelectedAppointment(appointment);
                controller.setSelectedAppointmentBaselineActivityId(appointment.lastActivity?.id ?? null);
              }}
              onComplete={(appointment) => {
                controller.updateMutation.mutate({
                  id: appointment.id,
                  input: { status: "completed" },
                  expectedActivityId: appointment.lastActivity?.id,
                });
              }}
              selectedAppointmentId={controller.selectedAppointment?.id ?? null}
            />
          </div>
        </section>
      </PageLayout>
      <AppointmentDetailsModal
        appointment={controller.currentSelectedAppointment}
        isStale={controller.isSelectedAppointmentStale}
        onClose={() => {
          controller.setSelectedAppointment(null);
          controller.setSelectedAppointmentBaselineActivityId(undefined);
        }}
        onEdit={(appointment) => {
          controller.setSelectedAppointment(null);
          controller.setSelectedAppointmentBaselineActivityId(undefined);
          controller.setAppointmentToEdit(appointment);
          controller.setAppointmentToEditBaselineActivityId(appointment.lastActivity?.id ?? null);
        }}
        onCreatePayment={
          controller.canCreateAppointments
            ? (appointment) => {
                controller.openPaymentCreateForAppointment(appointment);
              }
            : undefined
        }
        onStatusChange={(appointment, status) => {
          controller.updateMutation.mutate({
            id: appointment.id,
            input: { status },
            expectedActivityId: controller.selectedAppointmentBaselineActivityId ?? undefined,
          });
        }}
        onDelete={(appointment) => {
          if (appointment.recurringRule) {
            controller.setAppointmentToDelete(appointment);
            controller.setAppointmentToDeleteBaselineActivityId(
              controller.selectedAppointmentBaselineActivityId ?? appointment.lastActivity?.id ?? null,
            );
            return;
          }

          controller.modal.confirm({
            title: "Удалить запись?",
            onOk: () => {
              controller.deleteMutation.mutate({
                id: appointment.id,
                expectedActivityId: controller.selectedAppointmentBaselineActivityId ?? undefined,
              });
            },
          });
        }}
      />
      <VacationRangeModal
        initialPeriod={controller.vacationInitialPeriod}
        open={controller.vacationInitialPeriod !== null}
        pending={controller.vacationMutation.isPending || controller.removeVacationMutation.isPending}
        requestApproval={!controller.auth.user?.isSuperuser}
        editing={controller.editingVacationId !== null}
        removePending={controller.removeVacationMutation.isPending}
        subjectId={controller.effectiveProviderFilterId}
        subjectName={
          controller.effectiveProviderFilterId === controller.auth.user?.id
            ? [controller.auth.user?.lastName, controller.auth.user?.firstName].filter(Boolean).join(" ")
            : undefined
        }
        onCancel={controller.closeVacationModal}
        onRemove={controller.editingVacationId ? controller.confirmVacationRemoval : undefined}
        onSubmit={(values) => {
          controller.vacationMutation.mutate(values);
        }}
      />
      <RecurringDeleteModal
        appointment={controller.currentDeletingAppointment}
        deletePending={controller.deleteMutation.isPending}
        isStale={controller.isDeletingAppointmentStale}
        onCancel={() => {
          controller.setAppointmentToDelete(null);
          controller.setAppointmentToDeleteBaselineActivityId(undefined);
        }}
        onDelete={(appointment, scope) => {
          controller.deleteMutation.mutate({
            id: appointment.id,
            scope,
            expectedActivityId: controller.appointmentToDeleteBaselineActivityId ?? undefined,
          });
        }}
      />
      <RecurringRescheduleModal
        appointment={controller.currentReschedulingAppointment}
        nextStartDate={controller.appointmentToRescheduleStartDate}
        reschedulePending={controller.rescheduleMutation.isPending}
        isStale={controller.isReschedulingAppointmentStale}
        onCancel={() => {
          controller.setAppointmentToReschedule(null);
          controller.setAppointmentToRescheduleStartDate(null);
          controller.setAppointmentToRescheduleBaselineActivityId(undefined);
        }}
        onReschedule={(appointment, startDate, scope) => {
          controller.rescheduleMutation.mutate({
            appointment,
            startDate,
            scope,
            expectedActivityId: controller.currentReschedulingAppointment?.lastActivity?.id ?? undefined,
          });
        }}
      />
      <AppointmentCreateModal
        canCreateClient={controller.canCreateAppointments}
        createPending={controller.createMutation.isPending}
        createdClientOptions={controller.createdClientOptions}
        hasDraft={controller.hasCreateDraft}
        draftRestored={controller.isCreateDraftRestored && controller.isCreateModalOpen}
        draftSaveStatus={controller.createDraftSaveStatus}
        form={controller.form}
        lockedProviderId={controller.lockedProviderId}
        onCreateClient={() => {
          controller.setQuickClientCreateOpen(true);
        }}
        onCancel={controller.closeCreateModal}
        onDraftChange={controller.handleCreateDraftChange}
        onSubmit={(values) => {
          controller.createMutation.mutate(values);
        }}
        onClientLabelChange={controller.setCreateClientLabel}
        onServiceLabelChange={controller.setCreateServiceLabel}
        onProviderLabelChange={controller.setCreateProviderLabel}
        open={controller.isCreateModalOpen}
        onClearDraft={controller.handleClearCreateDraft}
        onRetryDraft={controller.createDraftRetry}
        recurrenceTypes={controller.recurrenceTypesQuery.data ?? []}
        recurrenceTypesLoading={controller.recurrenceTypesQuery.isLoading}
        courseThemeOptions={controller.createCourseThemeOptions}
        courseThemesLoading={controller.createCourseEnrollmentsQuery.isLoading}
      />
      <AppointmentEditModal
        appointment={controller.currentEditingAppointment}
        canCreateClient={controller.canCreateAppointments}
        createdClientOptions={controller.createdClientOptions}
        editPending={controller.editMutation.isPending}
        form={controller.editForm}
        isStale={controller.isEditingAppointmentStale}
        lockedProviderId={controller.lockedProviderId}
        courseThemeOptions={controller.editCourseThemeOptions}
        courseThemesLoading={controller.editCourseEnrollmentsQuery.isLoading}
        onCreateClient={() => {
          controller.setQuickClientCreateOpen(true);
        }}
        onCancel={() => {
          controller.setAppointmentToEdit(null);
          controller.setAppointmentToEditBaselineActivityId(undefined);
        }}
        onSubmit={(values) => {
          if (!controller.appointmentToEdit) {
            return;
          }

          controller.editMutation.mutate({
            id: controller.appointmentToEdit.id,
            input: values,
            expectedActivityId: controller.appointmentToEditBaselineActivityId ?? undefined,
          });
        }}
        onValuesChange={controller.onEditDraftChange}
        draftStatus={controller.editDraft.status}
        draftRestored={controller.editDraft.restored}
        hasDraft={controller.editDraft.hasDraft}
        draftStale={controller.editDraft.isStale}
        onReapplyDraft={controller.editDraft.reapply}
        onRetryDraft={controller.editDraft.retry}
        onDiscardDraft={() => {
          void controller.editDraft.discard().then(() => {
            const appointment = controller.currentEditingAppointment;
            if (appointment) {
              controller.editForm.setFieldsValue({
                clientId: appointment.client.id,
                serviceId: appointment.service.id,
                providerId: controller.lockedProviderId ?? appointment.provider?.id,
                courseThemeId: appointment.courseTheme?.id,
                lessonNotes: appointment.lessonNotes ?? undefined,
                startDate: dayjs(appointment.startDate),
              });
            }
          });
        }}
      />
      <ClientQuickCreateModal
        open={controller.isQuickClientCreateOpen}
        onCancel={() => {
          controller.setQuickClientCreateOpen(false);
        }}
        onCreated={controller.onQuickClientCreated}
      />
      <PaymentCreateModal
        open={controller.paymentCreate.isCreateModalOpen}
        editing={Boolean(controller.paymentCreate.editingPayment)}
        hasDraft={controller.paymentCreate.hasCreateDraft}
        draftRestored={controller.paymentCreate.isCreateDraftRestored && controller.paymentCreate.isCreateModalOpen}
        draftSaveStatus={controller.paymentCreate.createDraftSaveStatus}
        form={controller.paymentCreate.form}
        createPending={controller.paymentCreate.saveMutation.isPending}
        createdClientOptions={controller.paymentCreate.createdClientOptions}
        draftHydrationRef={controller.paymentCreate.draftHydrationRef}
        selectedCreateServiceId={controller.paymentCreate.selectedCreateServiceId}
        selectedServicePrice={controller.paymentCreate.selectedServicePrice}
        onCancel={controller.paymentCreate.closeCreateModal}
        onClearDraft={controller.paymentCreate.handleClearCreateDraft}
        draftStale={controller.paymentCreate.activeDraft.isStale}
        onReapplyDraft={controller.paymentCreate.activeDraft.reapply}
        onRetryDraft={controller.paymentCreate.activeDraft.retry}
        onSubmit={(values) => {
          controller.paymentCreate.saveMutation.mutate({
            values,
            expectedActivityId: controller.paymentCreate.editingBaselineActivityId ?? undefined,
          });
        }}
        onValuesChange={controller.paymentCreate.onCreateValuesChange}
        onCreateClient={() => {
          controller.paymentCreate.setQuickClientCreateOpen(true);
        }}
        onClientLabelChange={controller.paymentCreate.setCreateClientLabel}
        onServiceLabelChange={controller.paymentCreate.setCreateServiceLabel}
        onServicePriceChange={controller.paymentCreate.setSelectedServicePrice}
      />
      <ClientQuickCreateModal
        open={controller.paymentCreate.isQuickClientCreateOpen}
        onCancel={() => {
          controller.paymentCreate.setQuickClientCreateOpen(false);
        }}
        onCreated={controller.paymentCreate.onQuickClientCreated}
      />
    </>
  );
}

function formatScheduleWeekRange(weekStart: dayjs.Dayjs) {
  const start = weekStart.startOf("week");
  const end = weekStart.endOf("week");

  if (start.isSame(end, "month")) {
    return `${start.format("D")} - ${end.format("D MMMM YYYY")}`;
  }

  if (start.isSame(end, "year")) {
    return `${start.format("D MMMM")} - ${end.format("D MMMM YYYY")}`;
  }

  return `${start.format("D MMMM YYYY")} - ${end.format("D MMMM YYYY")}`;
}
