import { useQuery } from "@tanstack/react-query";
import dayjs from "dayjs";
import { useMemo } from "react";

import { getAppointmentStatusLabel } from "@/entities/appointment";
import { PortalCourseCard } from "@/entities/course";
import { useAuth } from "@/entities/session";
import { clientPortalApi, clientPortalQueryKeys, getPortalHomework, usePortalEnrollments } from "@/features/client-portal";
import { formatMoney } from "@/shared/lib";
import { RpgApiError, RpgBadge, RpgDivider, RpgPanel, RpgState } from "@/shared/ui/rpg";

import styles from "./ClientPortalHomePage.module.css";

export function ClientPortalHomePage() {
  const { user } = useAuth();
  const timezone = useMemo(() => Intl.DateTimeFormat().resolvedOptions().timeZone, []);
  const schedule = useQuery({
    queryKey: clientPortalQueryKeys.schedule(user?.linkedClientId, timezone),
    queryFn: ({ signal }) => clientPortalApi.schedule({ timezone }, signal),
    enabled: Boolean(user?.linkedClientId),
    refetchInterval: 60_000,
    refetchIntervalInBackground: false,
  });
  const courses = usePortalEnrollments();
  const appointment = schedule.data;
  const homework = getPortalHomework(courses.data ?? []);

  return (
    <div className={styles.page}>
      <header className={styles.intro}>
        <h1>Главная</h1>
        <p>Занятия, курсы и практика — всё для следующего шага.</p>
      </header>
      <div className={styles.summary}>
        <RpgPanel className={styles.stack}>
          <h2>Ближайшее занятие</h2>
          {schedule.isPending ? <RpgState kind="loading" title="Загружаем расписание" /> : null}
          {schedule.isError ? (
            <RpgApiError
              error={schedule.error}
              onRetry={() => {
                void schedule.refetch();
              }}
            />
          ) : null}
          {appointment ? (
            <>
              {schedule.isError ? <p>Показаны последние полученные данные.</p> : null}
              <p className={styles.date}>
                {dayjs(appointment.startDate).format("D MMMM, dddd · HH:mm")} — {dayjs(appointment.endDate).format("HH:mm")}
              </p>
              <div>
                <RpgBadge>{getAppointmentStatusLabel(appointment.status)}</RpgBadge>
              </div>
              {appointment.courseTheme ? <p>{appointment.courseTheme.title}</p> : null}
            </>
          ) : schedule.isSuccess ? (
            <p>Пока нет запланированных занятий.</p>
          ) : null}
        </RpgPanel>
        <RpgPanel className={styles.stack}>
          <h2>Баланс</h2>
          <p className={styles.balance} data-debt={(user?.balance ?? 0) < 0}>
            {formatMoney(user?.balance ?? 0)}
          </p>
        </RpgPanel>
      </div>
      <RpgDivider />
      <section className={styles.stack} aria-labelledby="home-courses">
        <h2 id="home-courses">Мои курсы</h2>
        {courses.isPending ? <RpgState kind="loading" title="Загружаем обучение" /> : null}
        {courses.isError ? (
          <RpgApiError
            error={courses.error}
            onRetry={() => {
              void courses.refetch();
            }}
          />
        ) : null}
        {courses.isSuccess && !courses.data.length ? (
          <RpgState kind="empty" title="Курсы ещё не назначены">
            Когда преподаватель назначит курс, он появится здесь.
          </RpgState>
        ) : null}
        <div className={styles.grid}>
          {courses.data?.map((course) => (
            <PortalCourseCard key={course.id} title={course.courseName} description={course.course.description ?? "Ваш курс обучения"} />
          ))}
        </div>
      </section>
      <RpgPanel className={styles.stack}>
        <h2>Домашняя практика</h2>
        <p>
          {courses.isSuccess
            ? homework.length
              ? `Заданий для практики: ${String(homework.length)}`
              : "Сейчас нет домашних заданий."
            : "Задания преподавателя появятся после загрузки обучения."}
        </p>
      </RpgPanel>
    </div>
  );
}
