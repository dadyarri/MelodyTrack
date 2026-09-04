namespace MelodyTrack.Core.Auditing;

public sealed record AuditCategoryDefinition
{
    internal AuditCategoryDefinition(string code, string label)
    {
        Code = code;
        Label = label;
    }

    public string Code { get; }
    public string Label { get; }
}

public sealed record AuditEventDefinition
{
    internal AuditEventDefinition(AuditCategoryDefinition category, string code, string label)
    {
        Category = category;
        Code = code;
        Label = label;
    }

    public AuditCategoryDefinition Category { get; }
    public string Code { get; }
    public string Label { get; }
}

public static class AuditCatalog
{
    public static class Categories
    {
        public static readonly AuditCategoryDefinition Authentication = new("auth", "Авторизация");
        public static readonly AuditCategoryDefinition Security = new("security", "Безопасность");
        public static readonly AuditCategoryDefinition Clients = new("clients", "Клиенты");
        public static readonly AuditCategoryDefinition Services = new("services", "Услуги");
        public static readonly AuditCategoryDefinition Payments = new("payments", "Платежи");
        public static readonly AuditCategoryDefinition Expenses = new("expenses", "Расходы");
        public static readonly AuditCategoryDefinition ExpenseCategories = new("expense_category", "Статьи расходов");
        public static readonly AuditCategoryDefinition Schedule = new("schedule", "Расписание");
        public static readonly AuditCategoryDefinition Users = new("users", "Пользователи");
        public static readonly AuditCategoryDefinition RecurringTasks = new("recurring_tasks", "Напоминания");
        public static readonly AuditCategoryDefinition Courses = new("courses", "Курсы");
        public static readonly AuditCategoryDefinition CourseEnrollments = new("course_enrollments", "Назначения курсов");
        public static readonly AuditCategoryDefinition CourseProgress = new("course_progress", "Прогресс по курсам");
        public static readonly AuditCategoryDefinition Initialization = new("initialization", "Инициализация");
        public static readonly AuditCategoryDefinition GodMode = new("god_mode", "Аварийный доступ");
        public static readonly AuditCategoryDefinition SystemNotices = new("system_notices", "Системные уведомления");
        public static readonly AuditCategoryDefinition VacationRequests = new("vacation_requests", "Заявки на отпуск");
        public static readonly AuditCategoryDefinition WorkingHoursRequests = new("working_hours_requests", "Заявки на изменение рабочих дней");
    }

    public static class Events
    {
        public static readonly AuditEventDefinition InviteCreated = Define(Categories.Authentication, "invite_created", "Создано приглашение");
        public static readonly AuditEventDefinition UserRegistered = Define(Categories.Authentication, "user_registered", "Пользователь зарегистрирован");
        public static readonly AuditEventDefinition LoginSucceeded = Define(Categories.Authentication, "login_succeeded", "Вход выполнен");
        public static readonly AuditEventDefinition LogoutSucceeded = Define(Categories.Authentication, "logout_succeeded", "Выход из сессии");
        public static readonly AuditEventDefinition LogoutAllSucceeded = Define(Categories.Authentication, "logout_all_succeeded", "Завершены все сессии");
        public static readonly AuditEventDefinition SessionRevoked = Define(Categories.Authentication, "session_revoked", "Сессия завершена");
        public static readonly AuditEventDefinition PasswordChanged = Define(Categories.Authentication, "password_changed", "Пароль изменён");
        public static readonly AuditEventDefinition PasswordResetLinkCreated = Define(Categories.Authentication, "password_reset_link_created", "Создана ссылка на восстановление пароля");
        public static readonly AuditEventDefinition PasswordResetCompleted = Define(Categories.Authentication, "password_reset_completed", "Пароль восстановлен");
        public static readonly AuditEventDefinition TwoFactorRemoved = Define(Categories.Authentication, "two_factor_removed", "2FA отключена");
        public static readonly AuditEventDefinition RecoveryCodesRegenerated = Define(Categories.Authentication, "recovery_codes_regenerated", "Коды восстановления обновлены");

        public static readonly AuditEventDefinition SuperuserBootstrapInviteAvailable = Define(Categories.Security, "superuser_bootstrap_invite_available", "Доступно bootstrap-приглашение суперпользователя");
        public static readonly AuditEventDefinition SuperuserRecoveryIssued = Define(Categories.Security, "superuser_recovery_issued", "Выпущено восстановление доступа суперпользователя");
        public static readonly AuditEventDefinition RefreshReplayDetected = Define(Categories.Security, "refresh_replay_detected", "Обнаружено повторное использование refresh-токена");
        public static readonly AuditEventDefinition UnusualSessionFanout = Define(Categories.Security, "unusual_session_fanout", "Обнаружено необычное число активных сессий");
        public static readonly AuditEventDefinition PortalPinFailed = Define(Categories.Security, "portal_pin_failed", "Неудачная проверка PIN клиентского кабинета");
        public static readonly AuditEventDefinition PortalPinRepeatedFailures = Define(Categories.Security, "portal_pin_repeated_failures", "Повторные ошибки PIN клиентского кабинета");

        public static readonly AuditEventDefinition ClientCreated = Define(Categories.Clients, "client_created", "Клиент создан");
        public static readonly AuditEventDefinition ClientUpdated = Define(Categories.Clients, "client_updated", "Клиент обновлён");
        public static readonly AuditEventDefinition ClientVacationsUpdated = Define(Categories.Clients, "client_vacations_updated", "Периоды отсутствия клиента обновлены");
        public static readonly AuditEventDefinition ClientVacationsUpdatedDirectly = Define(Categories.Clients, "client_vacations_updated_directly", "Суперпользователь напрямую обновил периоды отсутствия клиента");
        public static readonly AuditEventDefinition ClientDeleted = Define(Categories.Clients, "client_deleted", "Клиент удалён");
        public static readonly AuditEventDefinition ClientPortalLinkCreated = Define(Categories.Clients, "client_portal_link_created", "Создана ссылка на кабинет клиента");
        public static readonly AuditEventDefinition ClientPortalLinkRotated = Define(Categories.Clients, "client_portal_link_rotated", "Ссылка на кабинет клиента обновлена");
        public static readonly AuditEventDefinition ClientPortalLinkRevoked = Define(Categories.Clients, "client_portal_link_revoked", "Ссылка на кабинет клиента отозвана");
        public static readonly AuditEventDefinition ClientPortalPinReset = Define(Categories.Clients, "client_portal_pin_reset", "PIN кабинета клиента сброшен");
        public static readonly AuditEventDefinition LeadClosed = Define(Categories.Clients, "lead_closed", "Лид закрыт");
        public static readonly AuditEventDefinition LeadReopened = Define(Categories.Clients, "lead_reopened", "Лид возвращён в работу");
        public static readonly AuditEventDefinition ClientSourceCreated = Define(Categories.Clients, "client_source_created", "Источник клиента создан");
        public static readonly AuditEventDefinition ClientSourceDeleted = Define(Categories.Clients, "client_source_deleted", "Источник клиента удалён");

        public static readonly AuditEventDefinition ServiceCreated = Define(Categories.Services, "service_created", "Услуга создана");
        public static readonly AuditEventDefinition ServiceUpdated = Define(Categories.Services, "service_updated", "Услуга обновлена");
        public static readonly AuditEventDefinition ServiceDeleted = Define(Categories.Services, "service_deleted", "Услуга удалена");
        public static readonly AuditEventDefinition ServicePriceUpdated = Define(Categories.Services, "service_price_updated", "Цена услуги изменена");
        public static readonly AuditEventDefinition PaymentCreated = Define(Categories.Payments, "payment_created", "Платёж создан");
        public static readonly AuditEventDefinition PaymentUpdated = Define(Categories.Payments, "payment_updated", "Платёж изменён");
        public static readonly AuditEventDefinition PaymentDeleted = Define(Categories.Payments, "payment_deleted", "Платёж удалён");
        public static readonly AuditEventDefinition ExpenseCreated = Define(Categories.Expenses, "expense_created", "Расход создан");
        public static readonly AuditEventDefinition ExpenseUpdated = Define(Categories.Expenses, "expense_updated", "Расход изменён");
        public static readonly AuditEventDefinition ExpenseDeleted = Define(Categories.Expenses, "expense_deleted", "Расход удалён");
        public static readonly AuditEventDefinition ExpenseCategoryCreated = Define(Categories.ExpenseCategories, "expense_category_created", "Статья расхода создана");
        public static readonly AuditEventDefinition ExpenseCategoryDeleted = Define(Categories.Expenses, "expense_category_deleted", "Статья расхода удалена");

        public static readonly AuditEventDefinition AppointmentCreated = Define(Categories.Schedule, "appointment_created", "Занятие создано");
        public static readonly AuditEventDefinition RecurringAppointmentCreated = Define(Categories.Schedule, "recurring_appointment_created", "Повторяющееся занятие создано");
        public static readonly AuditEventDefinition AppointmentUpdated = Define(Categories.Schedule, "appointment_updated", "Занятие обновлено");
        public static readonly AuditEventDefinition RecurringAppointmentDetachedAndUpdated = Define(Categories.Schedule, "recurring_appointment_detached_and_updated", "Повторяющееся занятие изменено отдельно");
        public static readonly AuditEventDefinition AppointmentDeleted = Define(Categories.Schedule, "appointment_deleted", "Встреча удалена");
        public static readonly AuditEventDefinition RecurringAppointmentsRescheduled = Define(Categories.Schedule, "recurring_appointments_rescheduled", "Вся серия перенесена");
        public static readonly AuditEventDefinition RecurringAppointmentsSplitAndRescheduled = Define(Categories.Schedule, "recurring_appointments_split_and_rescheduled", "Серия разделена и перенесена");
        public static readonly AuditEventDefinition AppointmentsDeletedThisAndFollowing = Define(Categories.Schedule, "appointments_deleted_this_and_following", "Удалены эта и следующие занятия");
        public static readonly AuditEventDefinition AppointmentsDeletedAll = Define(Categories.Schedule, "appointments_deleted_all", "Удалена вся серия");
        public static readonly AuditEventDefinition AppointmentsDeletedSelectedWeekdayThisAndFollowing = Define(Categories.Schedule, "appointments_deleted_selected_weekday_this_and_following", "Удалены выбранный день и следующие");
        public static readonly AuditEventDefinition AppointmentsDeletedSelectedWeekdayAll = Define(Categories.Schedule, "appointments_deleted_selected_weekday_all", "Удалён выбранный день серии");

        public static readonly AuditEventDefinition UserUpdated = Define(Categories.Users, "user_updated", "Пользователь обновлён");
        public static readonly AuditEventDefinition UserAvailabilityUpdated = Define(Categories.Users, "user_availability_updated", "Доступность пользователя обновлена");
        public static readonly AuditEventDefinition UserWorkingHoursUpdatedDirectly = Define(Categories.Users, "user_working_hours_updated_directly", "Суперпользователь напрямую обновил рабочие дни пользователя");
        public static readonly AuditEventDefinition UserVacationsUpdatedDirectly = Define(Categories.Users, "user_vacations_updated_directly", "Суперпользователь напрямую обновил отпуска пользователя");
        public static readonly AuditEventDefinition RecurringTaskRuleUpdated = Define(Categories.RecurringTasks, "recurring_task_rule_updated", "Правило регулярной задачи обновлено");
        public static readonly AuditEventDefinition CustomTaskCreated = Define(Categories.RecurringTasks, "custom_task_created", "Пользовательская задача создана");
        public static readonly AuditEventDefinition TaskCompleted = Define(Categories.RecurringTasks, "task_completed", "Регулярная задача завершена");
        public static readonly AuditEventDefinition TaskCancelled = Define(Categories.RecurringTasks, "task_cancelled", "Регулярная задача отменена");
        public static readonly AuditEventDefinition TaskDelayed = Define(Categories.RecurringTasks, "task_delayed", "Регулярная задача отложена");
        public static readonly AuditEventDefinition CourseCreated = Define(Categories.Courses, "course_created", "Курс создан");
        public static readonly AuditEventDefinition CourseUpdated = Define(Categories.Courses, "course_updated", "Курс обновлён");
        public static readonly AuditEventDefinition CourseDeleted = Define(Categories.Courses, "course_deleted", "Курс удалён");
        public static readonly AuditEventDefinition CourseEnrollmentCreated = Define(Categories.CourseEnrollments, "course_enrollment_created", "Клиент записан на курс");
        public static readonly AuditEventDefinition CourseEnrollmentDeleted = Define(Categories.CourseEnrollments, "course_enrollment_deleted", "Клиент снят с курса");
        public static readonly AuditEventDefinition CourseThemeUnlocked = Define(Categories.CourseProgress, "course_theme_unlocked", "Тема курса открыта");
        public static readonly AuditEventDefinition CourseThemeStarted = Define(Categories.CourseProgress, "course_theme_started", "Работа над темой начата");
        public static readonly AuditEventDefinition CourseThemeSentToHomework = Define(Categories.CourseProgress, "course_theme_sent_to_homework", "Тема отправлена на домашнюю работу");
        public static readonly AuditEventDefinition CourseThemeHomeworkPassed = Define(Categories.CourseProgress, "course_theme_homework_passed", "Домашняя работа принята");
        public static readonly AuditEventDefinition CourseThemeReturnedToProgress = Define(Categories.CourseProgress, "course_theme_returned_to_progress", "Тема возвращена в работу");

        public static readonly AuditEventDefinition DevelopmentSeedV1 = Define(Categories.Initialization, "development_seed_v1", "Применены демонстрационные данные v1");
        public static readonly AuditEventDefinition DevelopmentSeedV2 = Define(Categories.Initialization, "development_seed_v2", "Применены демонстрационные данные v2");
        public static readonly AuditEventDefinition DevelopmentSeedV3 = Define(Categories.Initialization, "development_seed_v3", "Применены демонстрационные данные v3");
        public static readonly AuditEventDefinition DevelopmentSeedV4 = Define(Categories.Initialization, "development_seed_v4", "Применены демонстрационные данные v4");
        public static readonly AuditEventDefinition DevelopmentSeedV5 = Define(Categories.Initialization, "development_seed_v5", "Применены демонстрационные данные v5");
        public static readonly AuditEventDefinition DevelopmentSeedV6 = Define(Categories.Initialization, "development_seed_v6", "Применены демонстрационные данные v6");
        public static readonly AuditEventDefinition DevelopmentSeedV7 = Define(Categories.Initialization, "development_seed_v7", "Обновлены демонстрационные учетные данные v7");
        public static readonly AuditEventDefinition TestSeedV1 = Define(Categories.Initialization, "test_seed_v1", "Применены тестовые данные v1");

        public static readonly AuditEventDefinition GodModeLoginSucceeded = Define(Categories.GodMode, "god_mode_login_succeeded", "Выполнен вход в аварийный режим");
        public static readonly AuditEventDefinition GodModeLogoutSucceeded = Define(Categories.GodMode, "god_mode_logout_succeeded", "Завершена сессия аварийного режима");
        public static readonly AuditEventDefinition GodModeStateInspected = Define(Categories.GodMode, "god_mode_state_inspected", "Просмотрено состояние пользователей, сессий и восстановления");
        public static readonly AuditEventDefinition GodModePasswordResetRequired = Define(Categories.GodMode, "god_mode_password_reset_required", "Пользователю назначен обязательный сброс пароля");
        public static readonly AuditEventDefinition GodModePasswordResetLinkCreated = Define(Categories.GodMode, "god_mode_password_reset_link_created", "Создана аварийная ссылка сброса пароля");
        public static readonly AuditEventDefinition GodModePasswordResetLinksRevoked = Define(Categories.GodMode, "god_mode_password_reset_links_revoked", "Отозваны ссылки сброса пароля");
        public static readonly AuditEventDefinition GodModeSessionRevoked = Define(Categories.GodMode, "god_mode_session_revoked", "Аварийно завершена сессия");
        public static readonly AuditEventDefinition GodModeAllSessionsRevoked = Define(Categories.GodMode, "god_mode_all_sessions_revoked", "Аварийно завершены все сессии пользователя");
        public static readonly AuditEventDefinition GodModePortalPinReset = Define(Categories.GodMode, "god_mode_portal_pin_reset", "Аварийно сброшен PIN клиентского кабинета");
        public static readonly AuditEventDefinition GodModePortalLinkRotated = Define(Categories.GodMode, "god_mode_portal_link_rotated", "Аварийно обновлена ссылка клиентского кабинета");
        public static readonly AuditEventDefinition GodModePortalLinkRevoked = Define(Categories.GodMode, "god_mode_portal_link_revoked", "Аварийно отозвана ссылка клиентского кабинета");

        public static readonly AuditEventDefinition SystemNoticeCreated = Define(Categories.SystemNotices, "system_notice_created", "Системное уведомление создано");
        public static readonly AuditEventDefinition SystemNoticeUpdated = Define(Categories.SystemNotices, "system_notice_updated", "Системное уведомление обновлено");
        public static readonly AuditEventDefinition SystemNoticeExpired = Define(Categories.SystemNotices, "system_notice_expired", "Системное уведомление завершено");
        public static readonly AuditEventDefinition SystemNoticeDeleted = Define(Categories.SystemNotices, "system_notice_deleted", "Системное уведомление удалено");

        public static readonly AuditEventDefinition VacationRequestCreated = Define(Categories.VacationRequests, "vacation_request_created", "Заявка на отпуск создана");
        public static readonly AuditEventDefinition VacationRequestApproved = Define(Categories.VacationRequests, "vacation_request_approved", "Заявка на отпуск одобрена");
        public static readonly AuditEventDefinition VacationRequestDeclined = Define(Categories.VacationRequests, "vacation_request_declined", "Заявка на отпуск отклонена");
        public static readonly AuditEventDefinition VacationRequestCancelled = Define(Categories.VacationRequests, "vacation_request_cancelled", "Заявка на отпуск отменена");
        public static readonly AuditEventDefinition WorkingHoursRequestCreated = Define(Categories.WorkingHoursRequests, "working_hours_request_created", "Заявка на изменение рабочих дней создана");
        public static readonly AuditEventDefinition WorkingHoursRequestApproved = Define(Categories.WorkingHoursRequests, "working_hours_request_approved", "Изменение рабочих дней одобрено");
        public static readonly AuditEventDefinition WorkingHoursRequestDeclined = Define(Categories.WorkingHoursRequests, "working_hours_request_declined", "Заявка на изменение рабочих дней отклонена");
        public static readonly AuditEventDefinition WorkingHoursRequestCancelled = Define(Categories.WorkingHoursRequests, "working_hours_request_cancelled", "Заявка на изменение рабочих дней отменена");
    }

    public static IReadOnlyList<AuditCategoryDefinition> AllCategories { get; } = typeof(Categories)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Select(field => (AuditCategoryDefinition)field.GetValue(null)!)
        .ToArray();

    public static IReadOnlyList<AuditEventDefinition> AllEvents { get; } = typeof(Events)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Select(field => (AuditEventDefinition)field.GetValue(null)!)
        .ToArray();

    public static string GetCategoryLabel(string code) =>
        AllCategories.FirstOrDefault(category => category.Code == code)?.Label ?? code;

    public static string GetActionLabel(string code) =>
        AllEvents.FirstOrDefault(auditEvent => auditEvent.Code == code)?.Label ?? code;

    public static IReadOnlyCollection<string> FindCategoryCodes(string search) =>
        AllCategories.Where(category => category.Label.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Select(category => category.Code)
            .ToArray();

    public static IReadOnlyCollection<string> FindActionCodes(string search) =>
        AllEvents.Where(auditEvent => auditEvent.Label.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Select(auditEvent => auditEvent.Code)
            .ToArray();

    public static AuditEventDefinition GetEvent(string code) =>
        AllEvents.First(auditEvent => auditEvent.Code == code);

    private static AuditEventDefinition Define(AuditCategoryDefinition category, string code, string label) =>
        new(category, code, label);
}
