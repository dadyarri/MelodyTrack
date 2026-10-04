import { type ReactNode, useEffect } from "react";
import { useLocation } from "react-router";

import { StrummingTrainer, TrainerTimingHost } from "@/entities/trainer";
import { PortalCourseCard } from "@/entities/course";
import { getPortalHomework, usePortalEnrollments } from "@/features/client-portal";
import { mapExerciseDtos, StructuredHomeworkContent } from "@/features/homework-document";
import { RpgApiError, RpgBadge, RpgPanel, RpgState } from "@/shared/ui/rpg";

import styles from "./ClientPortalLearningPages.module.css";

function LearningPage({ title, children }: { title: string; children: ReactNode }) {
  return (
    <TrainerTimingHost><div className={styles.page}>
      <h1>{title}</h1>
      {children}
    </div></TrainerTimingHost>
  );
}

export function ClientPortalCoursesPage() {
  const query = usePortalEnrollments();
  const { hash } = useLocation();
  useEffect(() => {
    if (hash.startsWith("#course-")) document.getElementById(hash.slice(1))?.scrollIntoView();
  }, [hash]);
  return (
    <LearningPage title="Мои курсы">
      <p>Курсы, назначенные вашим преподавателем.</p>
      {query.isPending ? <RpgState kind="loading" title="Загружаем курсы" /> : null}
      {query.isError ? (
        <RpgApiError
          error={query.error}
          onRetry={() => {
            void query.refetch();
          }}
        />
      ) : null}
      {query.isSuccess && !query.data.length ? (
        <RpgState kind="empty" title="Курсы ещё не назначены">
          Обсудите с преподавателем, с чего начать обучение.
        </RpgState>
      ) : null}
      <div className={styles.courses}>
        {query.data?.map((course) => (
          <section key={course.id} id={`course-${course.id}`} className={styles.course}>
            <PortalCourseCard title={course.courseName} description={course.course.description ?? "Ваш курс обучения"}>
              {course.course.blocks.length ? (
                <details className={styles.details}>
                  <summary>Программа курса</summary>
                  {course.course.blocks.map((block) => (
                    <details key={block.id} className={styles.details}>
                      <summary>{block.title}</summary>
                      {block.branches.map((branch) => (
                        <div key={branch.id} className={styles.branch}>
                          <h3>{branch.title}</h3>
                          <ul>
                            {branch.themes.map((theme) => (
                              <li key={theme.id}>{theme.title}</li>
                            ))}
                          </ul>
                        </div>
                      ))}
                    </details>
                  ))}
                </details>
              ) : null}
            </PortalCourseCard>
          </section>
        ))}
      </div>
    </LearningPage>
  );
}

export function ClientPortalHomeworkPage() {
  const query = usePortalEnrollments();
  const assignments = getPortalHomework(query.data ?? []);
  return (
    <LearningPage title="Домашние задания">
      <p>Практика, которую назначил преподаватель. Выполненное задание обсудите на занятии.</p>
      {query.isPending ? <RpgState kind="loading" title="Загружаем задания" /> : null}
      {query.isError ? (
        <RpgApiError
          error={query.error}
          onRetry={() => {
            void query.refetch();
          }}
        />
      ) : null}
      {query.isSuccess && !assignments.length ? (
        <RpgState kind="empty" title="Пока нет домашних заданий">
          Новые задания появятся после назначения преподавателем.
        </RpgState>
      ) : null}
      {assignments.map(({ enrollmentId, courseName, theme }) => (
        <RpgPanel key={`${enrollmentId}-${theme.id}`} className={styles.assignment}>
          <p>{courseName}</p>
          <h2>{theme.themeTitle}</h2>
          <div>
            <RpgBadge>Домашняя практика</RpgBadge>
          </div>
          <StructuredHomeworkContent
            value={theme.homeworkDocument}
            exercises={mapExerciseDtos(theme.practiceExercises)}
            className={styles.homeworkContent}
          />
        </RpgPanel>
      ))}
    </LearningPage>
  );
}

export function ClientPortalTrainersPage() {
  return (
    <LearningPage title="Тренажёры">
      <p>Самостоятельная практика: настраивайте рисунок, темп и метроном. Эти настройки не изменяют задания преподавателя.</p>
      <StrummingTrainer />
    </LearningPage>
  );
}

export function ClientPortalLeaderboardsPage() {
  return (
    <LearningPage title="Рейтинги">
      <RpgState kind="empty" title="Рейтинги ещё не открыты">
        Здесь появятся результаты участников. Продолжайте обучение в своём темпе.
      </RpgState>
    </LearningPage>
  );
}
