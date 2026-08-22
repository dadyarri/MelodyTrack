import { Drawer } from "antd";

import { type Client, type ClientHistory, formatClientName } from "@/entities/client";
import type { CourseEnrollment, CourseEnrollmentThemeProgressAction } from "@/entities/course";
import { QueryStateBlock } from "@/shared/ui";

import { ClientHistoryPanel } from "./ClientHistoryPanel";

type ClientHistoryDrawerProps = {
  client: Client | null;
  data?: ClientHistory;
  isLoading: boolean;
  error?: unknown;
  courseEnrollments?: CourseEnrollment[];
  isCourseEnrollmentsLoading?: boolean;
  isCourseEnrollmentsError?: boolean;
  onClose: () => void;
  onCreateAppointment?: (client: ClientHistory["client"]) => void;
  onCreatePayment?: (client: ClientHistory["client"]) => void;
  onCreateCourseEnrollment?: () => void;
  onCreatePortalLink?: () => void;
  isCreatingPortalLink?: boolean;
  onRevokePortalLink?: () => void;
  isRevokingPortalLink?: boolean;
  onCreateCalendarSubscription?: () => void;
  isCreatingCalendarSubscription?: boolean;
  onResetPortalPin?: () => void;
  isResettingPortalPin?: boolean;
  onDeleteCourseEnrollment?: (enrollmentId: string) => void;
  onOpenCourseProgress?: (enrollmentId?: string) => void;
  onUpdateThemeProgress?: (themeId: string, action: CourseEnrollmentThemeProgressAction) => void;
  onEventsPageChange?: (page: number) => void;
  onEditVacations?: (client: Client) => void;
};

export function ClientHistoryDrawer({
  client,
  data,
  isLoading,
  error,
  courseEnrollments,
  isCourseEnrollmentsLoading = false,
  isCourseEnrollmentsError = false,
  onClose,
  onCreateAppointment,
  onCreatePayment,
  onCreateCourseEnrollment,
  onCreatePortalLink,
  isCreatingPortalLink,
  onRevokePortalLink,
  isRevokingPortalLink,
  onCreateCalendarSubscription,
  isCreatingCalendarSubscription,
  onResetPortalPin,
  isResettingPortalPin,
  onDeleteCourseEnrollment,
  onOpenCourseProgress,
  onUpdateThemeProgress,
  onEventsPageChange,
  onEditVacations,
}: ClientHistoryDrawerProps) {
  return (
    <Drawer
      title={client ? `История клиента: ${formatClientName(client)}` : "История клиента"}
      size={736}
      open={Boolean(client)}
      onClose={onClose}
      destroyOnHidden
    >
      {data ? (
        <ClientHistoryPanel
          data={data}
          courseEnrollments={courseEnrollments}
          isCourseEnrollmentsLoading={isCourseEnrollmentsLoading}
          isCourseEnrollmentsError={isCourseEnrollmentsError}
          onCreateAppointment={onCreateAppointment}
          onCreatePayment={onCreatePayment}
          onCreateCourseEnrollment={onCreateCourseEnrollment}
          onCreatePortalLink={onCreatePortalLink}
          isCreatingPortalLink={isCreatingPortalLink}
          onRevokePortalLink={onRevokePortalLink}
          isRevokingPortalLink={isRevokingPortalLink}
          onCreateCalendarSubscription={onCreateCalendarSubscription}
          isCreatingCalendarSubscription={isCreatingCalendarSubscription}
          onResetPortalPin={onResetPortalPin}
          isResettingPortalPin={isResettingPortalPin}
          onDeleteCourseEnrollment={onDeleteCourseEnrollment}
          onOpenCourseProgress={onOpenCourseProgress}
          onUpdateThemeProgress={onUpdateThemeProgress}
          onEventsPageChange={onEventsPageChange}
          onEditVacations={onEditVacations}
        />
      ) : null}
      <QueryStateBlock
        isLoading={isLoading}
        error={error}
        isEmpty={!isLoading && !error && !data}
        loadingText="Загрузка истории..."
        emptyText="История клиента пока недоступна"
        errorMessage="Не удалось загрузить историю клиента."
      />
    </Drawer>
  );
}
