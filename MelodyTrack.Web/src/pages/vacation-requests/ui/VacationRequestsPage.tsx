import { hasSuperuserAccess, useAuth } from "@/entities/session";
import { VacationRequestWorkspace, WorkingHoursRequestWorkspace } from "@/features/manage-vacation-requests";
import { PageLayout } from "@/shared/ui";

export function VacationRequestsPage() {
  const auth = useAuth();
  const review = hasSuperuserAccess(auth.user);

  return (
    <PageLayout
      title={review ? "Заявки на доступность" : "Мои заявки"}
      description={
        review
          ? "Принимайте решения по отпускам и изменениям рабочих дней сотрудников."
          : "Отправляйте заявки на отпуск и отслеживайте согласование рабочих дней."
      }
      size={20}
    >
      <VacationRequestWorkspace mode={review ? "review" : "staff"} />
      <WorkingHoursRequestWorkspace mode={review ? "review" : "staff"} />
    </PageLayout>
  );
}
