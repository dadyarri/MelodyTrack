import type { OnboardingJourney, OnboardingStepDefinition } from "../model/types";

export const teacherJourney = {
  id: "teacher",
  steps: [
    {
      id: "teacher-welcome",
      path: "/",
      title: "Добро пожаловать в MelodyTrack",
      description: "За пару шагов покажем, где посмотреть ближайшие занятия и настроить своё расписание.",
    },
    {
      id: "teacher-overview",
      path: "/",
      targetId: "dashboard-content",
      placement: "top",
      title: "Начните с обзора",
      description: "Здесь видны занятия на сегодня и завтра. Откройте нужную запись, чтобы быстро вспомнить детали.",
    },
    {
      id: "teacher-schedule",
      path: "/schedule",
      targetId: "schedule-calendar",
      placement: "top",
      title: "Ваше расписание",
      description: "Календарь помогает проверить занятость и открыть любое занятие.",
    },
    {
      id: "teacher-availability",
      path: "/profile",
      targetId: "profile-availability",
      placement: "top",
      title: "Рабочее время",
      description: "Укажите здесь обычные рабочие часы и отпуска, чтобы расписание учитывало вашу доступность.",
    },
  ],
} satisfies OnboardingJourney;

const administratorSteps = [
  {
    id: "administrator-welcome",
    path: "/",
    title: "Давайте быстро освоимся",
    description: "Покажем, где начать рабочий день и как перейти к главным действиям.",
  },
  {
    id: "administrator-tasks",
    path: "/tasks",
    targetId: "tasks-content",
    placement: "top",
    title: "Задачи без лишних заметок",
    description: "Здесь собраны напоминания и текущие дела. Их можно завершить, отложить или добавить вручную.",
  },
  {
    id: "administrator-schedule",
    path: "/schedule",
    targetId: "schedule-header-actions",
    placement: "bottom",
    title: "Запись на занятие",
    description: "Создайте занятие кнопкой вверху. Уже добавленные записи открываются прямо из календаря.",
  },
  {
    id: "administrator-clients",
    path: "/clients",
    targetId: "clients-page-content",
    placement: "top",
    title: "Всё о клиенте",
    description: "Найдите клиента, откройте его историю и при необходимости сразу добавьте занятие или платёж.",
  },
  {
    id: "administrator-courses",
    path: "/courses",
    targetId: "courses-workspace",
    placement: "top",
    title: "Курсы и прогресс",
    description: "Соберите программу курса, запишите клиента и отмечайте пройденные темы по ходу обучения.",
  },
  {
    id: "administrator-analytics",
    path: "/statistics/work",
    targetId: "statistics-main",
    placement: "top",
    title: "Результаты работы",
    description: "Выберите период и посмотрите работу, финансы или клиентов. Фильтры сохраняются при переходе между отчётами.",
  },
] satisfies readonly OnboardingStepDefinition[];

export const administratorJourney = {
  id: "administrator",
  steps: administratorSteps,
} satisfies OnboardingJourney;

export const superuserJourney = {
  id: "superuser",
  steps: [
    ...administratorSteps.map((step) => ({ ...step, id: `superuser-${step.id}` })),
    {
      id: "superuser-users",
      path: "/users",
      targetId: "users-page-content",
      placement: "top",
      title: "Доступ для команды",
      description: "Здесь можно пригласить сотрудника и проверить, кому открыт доступ к MelodyTrack.",
    },
    {
      id: "superuser-audit",
      path: "/audit",
      targetId: "audit-page-content",
      placement: "top",
      title: "История важных действий",
      description: "Журнал помогает найти изменения и понять, кто и когда их внёс.",
    },
  ],
} satisfies OnboardingJourney;
