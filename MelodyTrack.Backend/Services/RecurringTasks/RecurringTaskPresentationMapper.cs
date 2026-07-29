using MelodyTrack.Backend.Api.Tasks.Responses;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;

namespace MelodyTrack.Backend.Services.RecurringTasks;

internal static class RecurringTaskPresentationMapper
{
    public static RecurringTaskDto MapCandidate(RecurringTaskCandidate candidate)
    {
        return new RecurringTaskDto
        {
            RuleId = candidate.RuleId,
            Type = candidate.Type.ToApiKey(),
            RecipientType = MapRecipientType(candidate.RecipientType),
            DeduplicationKey = candidate.DeduplicationKey,
            ClientId = candidate.ClientId,
            TeacherId = candidate.TeacherId,
            AppointmentId = candidate.AppointmentId,
            Title = candidate.Title,
            RelatedPersonDisplayName = candidate.RelatedPersonDisplayName,
            RelevantAtUtc = candidate.RelevantAtUtc,
            DelayedUntilUtc = null,
            BusinessDate = candidate.BusinessDate,
            Phone = candidate.Phone,
            Telegram = candidate.Telegram,
            Vk = candidate.Vk,
            PreparedMessage = candidate.PreparedMessage
        };
    }

    public static RecurringTaskDto MapExecution(RecurringTaskExecution execution)
    {
        var type = execution.Rule.Type;
        var relatedPersonDisplayName = execution.RecipientType == RecurringTaskRecipientType.Teacher
            ? FormatTeacherName(execution.Teacher)
            : FormatClientName(execution.Client);

        return new RecurringTaskDto
        {
            RuleId = execution.RuleId,
            Type = type.ToApiKey(),
            RecipientType = MapRecipientType(execution.RecipientType),
            DeduplicationKey = execution.DeduplicationKey,
            ClientId = execution.ClientId,
            TeacherId = execution.TeacherId,
            AppointmentId = execution.AppointmentId,
            Title = GetTaskTitle(type),
            RelatedPersonDisplayName = relatedPersonDisplayName,
            RelevantAtUtc = execution.Appointment?.StartDate,
            DelayedUntilUtc = execution.DelayedUntilUtc,
            BusinessDate = execution.BusinessDate,
            Phone = execution.RecipientType == RecurringTaskRecipientType.Teacher ? execution.Teacher?.Phone : execution.Client?.Contacts.Phone,
            Telegram = execution.RecipientType == RecurringTaskRecipientType.Teacher ? execution.Teacher?.Telegram : execution.Client?.Contacts.Telegram,
            Vk = execution.RecipientType == RecurringTaskRecipientType.Teacher ? execution.Teacher?.Vk : execution.Client?.Contacts.Vk,
            PreparedMessage = execution.GeneratedText ?? string.Empty
        };
    }

    public static RecurringTaskCandidate MapCustomTaskCandidate(CustomTask task, string timezone)
    {
        var localDueAt = DateTimeUtils.ConvertDateToTimezone(task.DelayedUntilUtc ?? task.DueAtUtc, timezone);
        return new RecurringTaskCandidate
        {
            RuleId = task.Id,
            Type = RecurringTaskType.CustomTask,
            RecipientType = task.ClientId.HasValue ? RecurringTaskRecipientType.Client : RecurringTaskRecipientType.External,
            DeduplicationKey = BuildCustomTaskDeduplicationKey(task.Id),
            ClientId = task.ClientId,
            TeacherId = null,
            AppointmentId = null,
            Title = task.Title,
            RelatedPersonDisplayName = task.Client is not null ? FormatClientName(task.Client) : task.RecipientName,
            RelevantAtUtc = task.DueAtUtc,
            BusinessDate = DateOnly.FromDateTime(localDueAt),
            Phone = task.Client?.Contacts.Phone ?? task.Phone,
            Telegram = task.Client?.Contacts.Telegram ?? task.Telegram,
            Vk = task.Client?.Contacts.Vk ?? task.Vk,
            PreparedMessage = task.MessageText,
            SortAtUtc = task.DelayedUntilUtc ?? task.DueAtUtc
        };
    }

    public static RecurringTaskDto MapCustomTaskExecution(CustomTask task, string timezone)
    {
        var localRelevantAt = DateTimeUtils.ConvertDateToTimezone(task.DueAtUtc, timezone);
        return new RecurringTaskDto
        {
            RuleId = task.Id,
            Type = RecurringTaskType.CustomTask.ToApiKey(),
            RecipientType = task.ClientId.HasValue ? "client" : "external",
            DeduplicationKey = BuildCustomTaskDeduplicationKey(task.Id),
            ClientId = task.ClientId,
            TeacherId = null,
            AppointmentId = null,
            Title = task.Title,
            RelatedPersonDisplayName = task.Client is not null ? FormatClientName(task.Client) : task.RecipientName,
            RelevantAtUtc = task.DueAtUtc,
            DelayedUntilUtc = task.DelayedUntilUtc,
            BusinessDate = DateOnly.FromDateTime(localRelevantAt),
            Phone = task.Client?.Contacts.Phone ?? task.Phone,
            Telegram = task.Client?.Contacts.Telegram ?? task.Telegram,
            Vk = task.Client?.Contacts.Vk ?? task.Vk,
            PreparedMessage = task.MessageText
        };
    }

    public static string FormatClientName(Client? client)
    {
        if (client is null)
        {
            return "Клиент";
        }

        return string.Join(' ', new[] { client.LastName, client.FirstName, client.Patronymic }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    public static string BuildCustomTaskDeduplicationKey(Ulid taskId) => $"custom-task:{taskId}";

    private static string MapRecipientType(RecurringTaskRecipientType recipientType) =>
        recipientType switch
        {
            RecurringTaskRecipientType.Teacher => "teacher",
            RecurringTaskRecipientType.External => "external",
            _ => "client"
        };

    private static string GetTaskTitle(RecurringTaskType type) =>
        type switch
        {
            RecurringTaskType.AppointmentReminder => "Напомнить о записи",
            RecurringTaskType.BirthdayGreeting => "Поздравить с днём рождения",
            RecurringTaskType.TrialFollowUp => "Связаться после пробного занятия",
            RecurringTaskType.InactiveClientReminder => "Напомнить о занятиях",
            RecurringTaskType.TeacherDailySchedule => "Отправить расписание",
            RecurringTaskType.DebtorReminder => "Напомнить о долге",
            RecurringTaskType.CustomTask => "Пользовательская задача",
            _ => "Задача"
        };

    private static string FormatTeacherName(User? teacher)
    {
        if (teacher is null)
        {
            return "Преподаватель";
        }

        return string.Join(' ', new[] { teacher.LastName, teacher.FirstName }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}
