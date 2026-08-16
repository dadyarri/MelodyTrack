import type { RecurringTaskType } from "@/entities/task";

export function getRecurringTaskTypeLabel(type: RecurringTaskType) {
  switch (type) {
    case "appointment-reminder":
      return "Напоминание о записи";
    case "birthday-greeting":
      return "Поздравление с днем рождения";
    case "trial-follow-up":
      return "Связаться после пробного";
    case "inactive-client-reminder":
      return "Вернуть клиента";
    case "teacher-daily-schedule":
      return "Расписание преподавателя";
    case "debtor-reminder":
      return "Напомнить о долге";
    case "custom-task":
      return "Пользовательская задача";
  }
}
