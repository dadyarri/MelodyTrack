using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.ClientPortal.Requests;
using MelodyTrack.Backend.Api.ClientSources.Requests;
using MelodyTrack.Backend.Api.Clients.Requests;
using MelodyTrack.Backend.Api.CourseEnrollments.Requests;
using MelodyTrack.Backend.Api.Courses.Requests;
using MelodyTrack.Backend.Api.Expenses.Requests;
using MelodyTrack.Backend.Api.Payments.Requests;
using MelodyTrack.Backend.Api.Releases.Requests;
using MelodyTrack.Backend.Api.Schedule.Requests;
using MelodyTrack.Backend.Api.Tasks.Requests;
using MelodyTrack.Backend.Api.Users.Requests;
using MelodyTrack.Backend.Api.VacationRequests.Requests;
using MelodyTrack.Backend.Api.WorkingHoursRequests.Requests;

namespace MelodyTrack.Backend.Validation;

internal static partial class RequestValidation
{
    private static readonly string[] AllowedDays =
    [
        "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"
    ];

    public static IEnumerable<ValidationResult> Validate(object request, ValidationContext context)
    {
        var errors = new List<ValidationResult>();
        switch (request)
        {
            case ChangePasswordRequest value:
                Required(errors, value.CurrentPassword, nameof(value.CurrentPassword), "Текущий пароль обязателен");
                Max(errors, value.CurrentPassword, 256, nameof(value.CurrentPassword), "Пароль слишком длинный");
                Password(errors, value.NewPassword, nameof(value.NewPassword), "Новый пароль", context);
                break;
            case CreateInviteRequest value:
                NotEmpty(errors, value.Role, nameof(value.Role), "Роль обязательна");
                Email(errors, value.Email, nameof(value.Email), "Невалидный email");
                break;
            case GetInviteCodeInformationRequest value:
                Required(errors, value.InviteCode, nameof(value.InviteCode), "Код приглашения обязателен");
                Max(errors, value.InviteCode, 128, nameof(value.InviteCode), "Код приглашения слишком длинный");
                break;
            case LoginRequest value:
                Required(errors, value.Email, nameof(value.Email), "Email обязателен");
                Email(errors, value.Email, nameof(value.Email), "Невалидный email");
                Required(errors, value.Password, nameof(value.Password), "Пароль обязателен");
                Max(errors, value.Password, 256, nameof(value.Password), "Пароль слишком длинный");
                Pattern(errors, value.Otp, SixDigitCode(), nameof(value.Otp), "Код 2FA должен содержать 6 цифр");
                Max(errors, value.RecoveryCode, 64, nameof(value.RecoveryCode), "Код восстановления слишком длинный");
                MutuallyExclusive(errors, value.Otp, value.RecoveryCode, nameof(value.Otp), "Используйте либо код 2FA, либо код восстановления");
                break;
            case Recover2FaRequest value:
                Required(errors, value.Email, nameof(value.Email), "Email обязателен");
                Email(errors, value.Email, nameof(value.Email), "Невалидный email");
                Required(errors, value.RecoveryCode, nameof(value.RecoveryCode), "Код восстановления обязателен");
                Max(errors, value.RecoveryCode, 64, nameof(value.RecoveryCode), "Код восстановления слишком длинный");
                break;
            case RegisterRequest value:
                Required(errors, value.Email, nameof(value.Email), "Email обязателен");
                Email(errors, value.Email, nameof(value.Email), "Невалидный email");
                Password(errors, value.Password, nameof(value.Password), "Пароль", context);
                break;
            case ResetPasswordRequest value:
                Required(errors, value.Token, nameof(value.Token), "Токен восстановления обязателен");
                Max(errors, value.Token, 512, nameof(value.Token), "Токен восстановления слишком длинный");
                Password(errors, value.NewPassword, nameof(value.NewPassword), "Пароль", context);
                Pattern(errors, value.Otp, SixDigitCode(), nameof(value.Otp), "Код 2FA должен содержать 6 цифр");
                Max(errors, value.RecoveryCode, 64, nameof(value.RecoveryCode), "Код восстановления слишком длинный");
                MutuallyExclusive(errors, value.Otp, value.RecoveryCode, nameof(value.Otp), "Используйте либо код 2FA, либо код восстановления");
                break;
            case Setup2FaRequest value:
                Required(errors, value.Password, nameof(value.Password), "Пароль обязателен");
                Max(errors, value.Password, 256, nameof(value.Password), "Пароль слишком длинный");
                break;
            case Verify2FaRequest value:
                Email(errors, value.Email, nameof(value.Email), "Невалидный email");
                Required(errors, value.Otp, nameof(value.Otp), "Код 2FA обязателен");
                Pattern(errors, value.Otp, SixDigitCode(), nameof(value.Otp), "Код 2FA должен содержать 6 цифр");
                Required(errors, value.OtpSecret, nameof(value.OtpSecret), "Секрет 2FA обязателен");
                Max(errors, value.OtpSecret, 256, nameof(value.OtpSecret), "Секрет 2FA слишком длинный");
                break;
            case AuthenticateClientPortalLinkRequest value:
                Required(errors, value.Token, nameof(value.Token), "Ссылка входа недействительна");
                Pattern(errors, value.Pin, FourDigitPin(), nameof(value.Pin), "PIN-код должен состоять из 4 цифр");
                Pattern(errors, value.PinConfirmation, FourDigitPin(), nameof(value.PinConfirmation), "Подтверждение PIN-кода должно состоять из 4 цифр");
                break;
            case AuthenticateSavedClientPortalIdentityRequest value:
                Required(errors, value.Reference, nameof(value.Reference), "Сохраненный профиль недействителен");
                Pattern(errors, value.Pin, FourDigitPin(), nameof(value.Pin), "PIN-код должен состоять из 4 цифр");
                break;
            case GetClientPortalLinkStatusRequest value:
                Required(errors, value.Token, nameof(value.Token), "Ссылка входа недействительна");
                break;
            case GetSavedClientPortalIdentityStatusRequest value:
                Required(errors, value.Reference, nameof(value.Reference), "Сохраненный профиль недействителен");
                break;
            case CreateClientSourceRequest value:
                Required(errors, value.Name, nameof(value.Name), "Название источника обязательно");
                Max(errors, value.Name, 200, nameof(value.Name), "Название источника должно быть не длиннее 200 символов");
                break;
            case CreateClientRequest value:
                Required(errors, value.FirstName, nameof(value.FirstName), "Имя обязательно");
                Required(errors, value.LastName, nameof(value.LastName), "Фамилия обязательна");
                Email(errors, value.Email, nameof(value.Email), "Укажите корректный email");
                break;
            case UpdateClientRequest value:
                foreach (var vacation in value.Vacations ?? [])
                {
                    if (vacation.StartDate > vacation.EndDate)
                    {
                        Add(errors, nameof(value.Vacations), "Дата окончания отсутствия не может быть раньше даты начала");
                    }
                }
                break;
            case CreateCourseEnrollmentRequest value:
                NotEmpty(errors, value.ClientId, nameof(value.ClientId), "Укажите клиента.");
                NotEmpty(errors, value.CourseId, nameof(value.CourseId), "Укажите курс.");
                break;
            case UpdateCourseEnrollmentThemeProgressRequest value:
                Required(errors, value.Action, nameof(value.Action), "Действие обязательно.");
                if (!string.IsNullOrWhiteSpace(value.Action)
                    && !CourseEnrollmentThemeProgressActionExtensions.TryParseApiKey(value.Action, out _))
                {
                    Add(errors, nameof(value.Action), "Некорректное действие прогресса.");
                }
                break;
            case CreateCourseRequest value:
                Course(errors, value.Name, value.Description, value.Levels, value.Blocks);
                break;
            case UpdateCourseRequest value:
                Course(errors, value.Name, value.Description, value.Levels, value.Blocks);
                break;
            case CreateExpenseRequest value:
                Expense(errors, value.Amount, value.Date, value.Description);
                break;
            case UpdateExpenseRequest value:
                Expense(errors, value.Amount, value.Date, value.Description);
                break;
            case CreatePaymentRequest value:
                Positive(errors, value.Amount, nameof(value.Amount), "Сумма платежа должна быть больше нуля");
                break;
            case UpdatePaymentRequest value:
                Positive(errors, value.Amount, nameof(value.Amount), "Сумма платежа должна быть больше нуля");
                break;
            case CreateAppointmentRequest value:
                Appointment(errors, value);
                break;
            case CreateCustomTaskRequest value:
                Required(errors, value.Title, nameof(value.Title), "Укажите заголовок задачи.");
                Max(errors, value.Title, 200, nameof(value.Title), "Заголовок задачи не должен быть длиннее 200 символов.");
                Required(errors, value.MessageText, nameof(value.MessageText), "Укажите текст задачи.");
                Max(errors, value.MessageText, 2000, nameof(value.MessageText), "Текст задачи не должен быть длиннее 2000 символов.");
                if (!value.ClientId.HasValue)
                {
                    Required(errors, value.RecipientName, nameof(value.RecipientName), "Укажите имя получателя для задачи без клиента.");
                }
                Max(errors, value.RecipientName, 200, nameof(value.RecipientName), "Имя получателя не должно быть длиннее 200 символов.");
                break;
            case UpdateRecurringTaskRuleRequest value:
                Required(errors, value.MessageTemplate, nameof(value.MessageTemplate), "Укажите текст шаблона.");
                Max(errors, value.MessageTemplate, 1000, nameof(value.MessageTemplate), "Текст шаблона не должен быть длиннее 1000 символов.");
                Positive(errors, value.OffsetMinutes, nameof(value.OffsetMinutes), "Смещение должно быть больше нуля.");
                Positive(errors, value.CooldownDays, nameof(value.CooldownDays), "Период повтора должен быть больше нуля.");
                break;
            case UpdateUserAvailabilityRequest value:
                Availability(errors, value);
                break;
            case UpdateUserRequest value:
                Required(errors, value.FirstName, nameof(value.FirstName), "Укажите имя пользователя.");
                Max(errors, value.FirstName, 128, nameof(value.FirstName), "Имя пользователя не должно быть длиннее 128 символов.");
                Required(errors, value.LastName, nameof(value.LastName), "Укажите фамилию пользователя.");
                Max(errors, value.LastName, 128, nameof(value.LastName), "Фамилия пользователя не должна быть длиннее 128 символов.");
                Max(errors, value.Phone, 32, nameof(value.Phone), "Телефон указан некорректно.");
                Max(errors, value.Telegram, 256, nameof(value.Telegram), "Telegram указан некорректно.");
                Max(errors, value.Vk, 256, nameof(value.Vk), "VK указан некорректно.");
                break;
            case CreateVacationRequest value:
                VacationRange(errors, value.StartDate, value.EndDate);
                Max(errors, value.Message, 500, nameof(value.Message), "Сообщение должно быть не длиннее 500 символов.");
                break;
            case CreateWorkingHoursRequest value:
                WorkingHours(errors, value.WorkingHours);
                Max(errors, value.Message, 500, nameof(value.Message), "Сообщение должно быть не длиннее 500 символов.");
                break;
            case VacationRequestDecisionRequest value:
                if (value.ExpectedVersion <= 0)
                {
                    Add(errors, nameof(value.ExpectedVersion), "Версия заявки указана некорректно.");
                }
                Max(errors, value.Message, 500, nameof(value.Message), "Комментарий должен быть не длиннее 500 символов.");
                break;
            case CancelVacationRequest value:
                if (value.ExpectedVersion <= 0)
                {
                    Add(errors, nameof(value.ExpectedVersion), "Версия заявки указана некорректно.");
                }
                break;
            case GetReleasesRequest value:
                if (value.Page is <= 0) Add(errors, nameof(value.Page), "Page должен быть больше нуля.");
                if (value.PageSize is < 1 or > 50) Add(errors, nameof(value.PageSize), "PageSize должен быть от 1 до 50.");
                break;
        }

        return errors;
    }

    private static void Password(List<ValidationResult> errors, string? value, string member, string displayName, ValidationContext context)
    {
        Required(errors, value, member, $"{displayName} не должен быть пустым");
        if (value is null) return;
        if (value.Length < 8) Add(errors, member, "Минимальная длина пароля — 8 символов");
        if (!StrongPassword().IsMatch(value))
        {
            Add(errors, member, "Пароль слишком простой: включите хотя бы одну заглавную латинскую букву, одну строчную, одну цифру и один спецсимвол");
        }
        if (context.GetService(typeof(ICommonPasswordService)) is ICommonPasswordService service && service.Contains(value))
        {
            Add(errors, member, "Пароль не должен быть частоиспользуемым");
        }
    }

    private static void Course(
        List<ValidationResult> errors,
        string? name,
        string? description,
        List<CreateCourseLevelRequest>? levels,
        List<CreateCourseBlockRequest>? blocks)
    {
        Required(errors, name, "Name", "Укажите название курса.");
        Max(errors, name, 200, "Name", "Название курса не должно быть длиннее 200 символов.");
        Max(errors, description, 2000, "Description", "Описание курса не должно быть длиннее 2000 символов.");

        foreach (var level in levels ?? [])
        {
            Required(errors, level.Title, "Levels", "Укажите название уровня.");
            Max(errors, level.Title, 200, "Levels", "Название уровня не должно быть длиннее 200 символов.");
            if (level.Order <= 0) Add(errors, "Levels", "Порядок уровня должен быть больше нуля.");
            if (level.RequiredExperiencePoints < 0) Add(errors, "Levels", "Порог опыта для уровня не может быть меньше нуля.");
        }

        foreach (var order in (levels ?? []).GroupBy(level => level.Order).Where(group => group.Count() > 1).Select(group => group.Key))
        {
            Add(errors, "Levels", $"Порядок уровня {order} должен быть уникальным.");
        }

        foreach (var block in blocks ?? [])
        {
            Required(errors, block.Title, "Blocks", "Укажите название блока.");
            Max(errors, block.Title, 200, "Blocks", "Название блока не должно быть длиннее 200 символов.");
            Max(errors, block.Description, 2000, "Blocks", "Описание блока не должно быть длиннее 2000 символов.");
            if (block.Order <= 0) Add(errors, "Blocks", "Порядок блока должен быть больше нуля.");
            foreach (var branch in block.Branches ?? [])
            {
                Required(errors, branch.Title, "Blocks", "Укажите название ветки.");
                Max(errors, branch.Title, 200, "Blocks", "Название ветки не должно быть длиннее 200 символов.");
                Max(errors, branch.Description, 2000, "Blocks", "Описание ветки не должно быть длиннее 2000 символов.");
                if (branch.Order <= 0) Add(errors, "Blocks", "Порядок ветки должен быть больше нуля.");
                foreach (var theme in branch.Themes ?? [])
                {
                    Required(errors, theme.Key, "Blocks", "Укажите ключ темы.");
                    Max(errors, theme.Key, 100, "Blocks", "Ключ темы не должен быть длиннее 100 символов.");
                    Required(errors, theme.Title, "Blocks", "Укажите название темы.");
                    Max(errors, theme.Title, 200, "Blocks", "Название темы не должно быть длиннее 200 символов.");
                    Max(errors, theme.Description, 4000, "Blocks", "Описание темы не должно быть длиннее 4000 символов.");
                    if (theme.Order <= 0) Add(errors, "Blocks", "Порядок темы должен быть больше нуля.");
                    if (theme.ExperiencePointsReward < 0) Add(errors, "Blocks", "Очки опыта не могут быть меньше нуля.");
                }
            }
        }

        CourseStructure(errors, blocks ?? []);
    }

    private static void CourseStructure(List<ValidationResult> errors, List<CreateCourseBlockRequest> blocks)
    {
        if (blocks.Select(block => block.Order).Distinct().Count() != blocks.Count)
        {
            Add(errors, "Blocks", "Порядок блоков должен быть уникальным.");
        }
        foreach (var block in blocks)
        {
            var branches = block.Branches ?? [];
            if (branches.Select(branch => branch.Order).Distinct().Count() != branches.Count)
            {
                Add(errors, "Blocks", $"Порядок веток в блоке \"{block.Title}\" должен быть уникальным.");
            }
            foreach (var branch in branches)
            {
                var themes = branch.Themes ?? [];
                if (themes.Select(theme => theme.Order).Distinct().Count() != themes.Count)
                {
                    Add(errors, "Blocks", $"Порядок тем в ветке \"{branch.Title}\" должен быть уникальным.");
                }
            }
        }

        var allThemes = blocks.SelectMany(block => block.Branches ?? []).SelectMany(branch => branch.Themes ?? []).ToList();
        foreach (var key in allThemes.GroupBy(theme => theme.Key, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key))
        {
            Add(errors, "Blocks", $"Ключ темы \"{key}\" должен быть уникальным.");
        }

        var keys = allThemes.Select(theme => theme.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var theme in allThemes)
        {
            graph[theme.Key] = [];
            foreach (var dependency in theme.DependencyKeys.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!keys.Contains(dependency))
                {
                    Add(errors, "Blocks", $"Тема \"{theme.Title}\" ссылается на неизвестную зависимость \"{dependency}\".");
                }
                else if (string.Equals(theme.Key, dependency, StringComparison.OrdinalIgnoreCase))
                {
                    Add(errors, "Blocks", $"Тема \"{theme.Title}\" не может зависеть сама от себя.");
                }
                else
                {
                    graph[theme.Key].Add(dependency);
                }
            }
        }
        if (HasCycle(graph, out var cycle))
        {
            var label = allThemes.FirstOrDefault(theme => string.Equals(theme.Key, cycle, StringComparison.OrdinalIgnoreCase))?.Title ?? cycle;
            Add(errors, "Blocks", $"Обнаружена циклическая зависимость тем. Проверьте тему \"{label}\".");
        }
    }

    private static bool HasCycle(IReadOnlyDictionary<string, List<string>> graph, out string cycle)
    {
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in graph.Keys)
        {
            if (HasCycle(node, graph, visiting, visited, out cycle)) return true;
        }
        cycle = string.Empty;
        return false;
    }

    private static bool HasCycle(string node, IReadOnlyDictionary<string, List<string>> graph, HashSet<string> visiting, HashSet<string> visited, out string cycle)
    {
        cycle = string.Empty;
        if (visited.Contains(node)) return false;
        if (!visiting.Add(node))
        {
            cycle = node;
            return true;
        }
        foreach (var dependency in graph.GetValueOrDefault(node, []))
        {
            if (HasCycle(dependency, graph, visiting, visited, out cycle)) return true;
        }
        visiting.Remove(node);
        visited.Add(node);
        return false;
    }

    private static void Expense(List<ValidationResult> errors, decimal amount, DateTime date, string? description)
    {
        if (amount < 0.01m) Add(errors, "Amount", "Сумма расхода должна быть больше нуля");
        if (decimal.Round(amount, 2) != amount) Add(errors, "Amount", "Сумма расхода может содержать не более двух знаков после запятой");
        if (date == default) Add(errors, "Date", "Укажите дату расхода");
        Required(errors, description, "Description", "Описание расхода не должно быть пустым");
    }

    private static void Appointment(List<ValidationResult> errors, CreateAppointmentRequest value)
    {
        NotEmpty(errors, value.ClientId, nameof(value.ClientId), "Идентификатор клиента не может быть пустым.");
        NotEmpty(errors, value.ServiceId, nameof(value.ServiceId), "Идентификатор услуги не может быть пустым.");
        Required(errors, value.Timezone, nameof(value.Timezone), "Нужно указать таймзону.");
        if (value.PatternEndDate.HasValue && value.StartDate > value.PatternEndDate.Value)
            Add(errors, nameof(value.StartDate), "Дата начала не может быть позже даты окончания шаблона.");
        if (value.RecurrenceTypeId.HasValue && !value.RecurrencePattern.HasValue)
            Add(errors, nameof(value.RecurrencePattern), "Шаблон повторения должен быть указан для повторяющейся записи.");
        if (!value.RecurrenceTypeId.HasValue && value.PatternEndDate.HasValue)
            Add(errors, nameof(value.PatternEndDate), "Дата окончания шаблона должна быть пустой для однократной записи.");
        if (!value.RecurrenceTypeId.HasValue && value.RecurrencePattern.HasValue)
            Add(errors, nameof(value.RecurrencePattern), "Шаблон повторения должен быть пустым для однократной записи.");
        Max(errors, value.LessonNotes, 4000, nameof(value.LessonNotes), "Заметки к уроку не должны быть длиннее 4000 символов.");
    }

    private static void Availability(List<ValidationResult> errors, UpdateUserAvailabilityRequest value)
    {
        if (value.WorkingHours is null || value.WorkingHours.Count != 7)
        {
            Add(errors, nameof(value.WorkingHours), "Нужно указать рабочие часы для всех дней недели.");
        }
        foreach (var day in value.WorkingHours ?? [])
        {
            if (string.IsNullOrWhiteSpace(day.DayOfWeek) || !AllowedDays.Contains(day.DayOfWeek.Trim().ToLowerInvariant()))
                Add(errors, nameof(value.WorkingHours), "Укажите корректный день недели.");
            if (day.IsWorkingDay && string.IsNullOrWhiteSpace(day.StartTime))
                Add(errors, nameof(value.WorkingHours), "Укажите время начала рабочего дня.");
            if (day.IsWorkingDay && string.IsNullOrWhiteSpace(day.EndTime))
                Add(errors, nameof(value.WorkingHours), "Укажите время окончания рабочего дня.");
            if (day.IsWorkingDay && (!TimeOnly.TryParse(day.StartTime, out var start) || !TimeOnly.TryParse(day.EndTime, out var end) || start >= end))
                Add(errors, nameof(value.WorkingHours), "Время работы указано некорректно.");
        }
        if (value.WorkingHours is not null
            && value.WorkingHours.Select(day => day.DayOfWeek?.Trim().ToLowerInvariant() ?? string.Empty).Distinct(StringComparer.Ordinal).Count() != 7)
            Add(errors, nameof(value.WorkingHours), "Каждый день недели должен быть указан ровно один раз.");
        foreach (var vacation in value.Vacations ?? [])
        {
            if (vacation.EndDate < vacation.StartDate)
                Add(errors, nameof(value.Vacations), "Дата окончания отпуска не может быть раньше даты начала.");
        }
    }

    private static void WorkingHours(List<ValidationResult> errors, IReadOnlyCollection<WorkingHoursRequestDayInput>? workingHours)
    {
        if (workingHours is null || workingHours.Count != 7)
        {
            Add(errors, nameof(CreateWorkingHoursRequest.WorkingHours), "Нужно указать рабочие часы для всех дней недели.");
        }
        foreach (var day in workingHours ?? [])
        {
            if (string.IsNullOrWhiteSpace(day.DayOfWeek) || !AllowedDays.Contains(day.DayOfWeek.Trim().ToLowerInvariant()))
                Add(errors, nameof(CreateWorkingHoursRequest.WorkingHours), "Укажите корректный день недели.");
            if (day.IsWorkingDay && string.IsNullOrWhiteSpace(day.StartTime))
                Add(errors, nameof(CreateWorkingHoursRequest.WorkingHours), "Укажите время начала рабочего дня.");
            if (day.IsWorkingDay && string.IsNullOrWhiteSpace(day.EndTime))
                Add(errors, nameof(CreateWorkingHoursRequest.WorkingHours), "Укажите время окончания рабочего дня.");
            if (day.IsWorkingDay && (!TimeOnly.TryParse(day.StartTime, out var start) || !TimeOnly.TryParse(day.EndTime, out var end) || start >= end))
                Add(errors, nameof(CreateWorkingHoursRequest.WorkingHours), "Время работы указано некорректно.");
        }
        if (workingHours is not null
            && workingHours.Select(day => day.DayOfWeek?.Trim().ToLowerInvariant() ?? string.Empty).Distinct(StringComparer.Ordinal).Count() != 7)
            Add(errors, nameof(CreateWorkingHoursRequest.WorkingHours), "Каждый день недели должен быть указан ровно один раз.");
    }

    private static void VacationRange(List<ValidationResult> errors, DateOnly startDate, DateOnly endDate)
    {
        if (startDate == default)
        {
            Add(errors, nameof(CreateVacationRequest.StartDate), "Укажите дату начала отпуска.");
        }
        if (endDate == default)
        {
            Add(errors, nameof(CreateVacationRequest.EndDate), "Укажите дату окончания отпуска.");
        }
        if (startDate != default && endDate != default && endDate < startDate)
        {
            Add(errors, nameof(CreateVacationRequest.EndDate), "Дата окончания отпуска не может быть раньше даты начала.");
        }
        if (endDate == DateOnly.MaxValue)
        {
            Add(errors, nameof(CreateVacationRequest.EndDate), "Дата окончания отпуска находится вне поддерживаемого диапазона.");
        }
    }

    private static void Required(List<ValidationResult> errors, string? value, string member, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) Add(errors, member, message);
    }

    private static void Max(List<ValidationResult> errors, string? value, int length, string member, string message)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Length > length) Add(errors, member, message);
    }

    private static void Email(List<ValidationResult> errors, string? value, string member, string message)
    {
        if (!string.IsNullOrWhiteSpace(value) && !new EmailAddressAttribute().IsValid(value)) Add(errors, member, message);
    }

    private static void Pattern(List<ValidationResult> errors, string? value, Regex regex, string member, string message)
    {
        if (!string.IsNullOrWhiteSpace(value) && !regex.IsMatch(value)) Add(errors, member, message);
    }

    private static void MutuallyExclusive(List<ValidationResult> errors, string? first, string? second, string member, string message)
    {
        if (!string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second)) Add(errors, member, message);
    }

    private static void NotEmpty(List<ValidationResult> errors, Ulid value, string member, string message)
    {
        if (value == Ulid.Empty) Add(errors, member, message);
    }

    private static void Positive(List<ValidationResult> errors, decimal value, string member, string message)
    {
        if (value <= 0) Add(errors, member, message);
    }

    private static void Positive(List<ValidationResult> errors, int? value, string member, string message)
    {
        if (value.HasValue && value.Value <= 0) Add(errors, member, message);
    }

    private static void Add(List<ValidationResult> errors, string member, string message) =>
        errors.Add(new ValidationResult(message, [member]));

    [GeneratedRegex("^\\d{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex SixDigitCode();

    [GeneratedRegex("^\\d{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex FourDigitPin();

    [GeneratedRegex("^(?=.*?[A-Z])(?=.*?[a-z])(?=.*?[0-9])(?=.*?[#?!@$%^&*-]).{8,}$", RegexOptions.CultureInvariant)]
    private static partial Regex StrongPassword();
}
