using MelodyTrack.Backend.Api.Tasks.Requests;
using MelodyTrack.Backend.Api.Tasks.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services.RecurringTasks;

public interface IRecurringTaskService
{
    Task<List<RecurringTaskDto>> GetTasksAsync(string timezone, RecurringTaskType? filterType, RecurringTaskListStatus status, CancellationToken ct);
    Task<RecurringTaskActionResult> CompleteAsync(CompleteRecurringTaskRequest request, User actor, CancellationToken ct);
    Task<RecurringTaskActionResult> SkipAsync(SkipRecurringTaskRequest request, User actor, CancellationToken ct);
}

public sealed class RecurringTaskActionResult
{
    public required bool Succeeded { get; init; }
    public required string ErrorMessage { get; init; }
    public required RecurringTaskStatus? Status { get; init; }

    public static RecurringTaskActionResult Success(RecurringTaskStatus status) =>
        new()
        {
            Succeeded = true,
            ErrorMessage = string.Empty,
            Status = status
        };

    public static RecurringTaskActionResult Failure(string message) =>
        new()
        {
            Succeeded = false,
            ErrorMessage = message,
            Status = null
        };
}

public class RecurringTaskService(AppDbContext db, IAuditLogService auditLogService) : IRecurringTaskService
{
    public async Task<List<RecurringTaskDto>> GetTasksAsync(string timezone, RecurringTaskType? filterType, RecurringTaskListStatus status, CancellationToken ct)
    {
        return status switch
        {
            RecurringTaskListStatus.Open => await GetOpenTasksAsync(timezone, filterType, ct),
            RecurringTaskListStatus.Completed => await GetProcessedTasksAsync(filterType, RecurringTaskStatus.Completed, ct),
            RecurringTaskListStatus.Skipped => await GetProcessedTasksAsync(filterType, RecurringTaskStatus.Skipped, ct),
            _ => []
        };
    }

    private async Task<List<RecurringTaskDto>> GetOpenTasksAsync(string timezone, RecurringTaskType? filterType, CancellationToken ct)
    {
        var rulesQuery = db.RecurringTaskRules
            .AsNoTracking()
            .Where(rule => rule.IsEnabled);

        if (filterType is { } type)
        {
            rulesQuery = rulesQuery.Where(rule => rule.Type == type);
        }

        var rules = await rulesQuery
            .OrderBy(rule => rule.Type)
            .ToListAsync(ct);

        var candidates = new List<RecurringTaskCandidate>();

        foreach (var rule in rules)
        {
            var ruleCandidates = rule.Type switch
            {
                RecurringTaskType.AppointmentReminder => await BuildAppointmentReminderCandidatesAsync(rule, timezone, ct),
                RecurringTaskType.BirthdayGreeting => await BuildBirthdayCandidatesAsync(rule, timezone, ct),
                RecurringTaskType.TrialFollowUp => await BuildTrialFollowUpCandidatesAsync(rule, timezone, ct),
                RecurringTaskType.InactiveClientReminder => await BuildInactiveClientCandidatesAsync(rule, timezone, ct),
                RecurringTaskType.TeacherDailySchedule => await BuildTeacherDailyScheduleCandidatesAsync(rule, timezone, ct),
                _ => []
            };

            candidates.AddRange(ruleCandidates);
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        var deduplicationKeys = candidates
            .Select(candidate => candidate.DeduplicationKey)
            .Distinct()
            .ToList();

        var handledKeys = await db.RecurringTaskExecutions
            .AsNoTracking()
            .Where(execution => deduplicationKeys.Contains(execution.DeduplicationKey))
            .Select(execution => execution.DeduplicationKey)
            .ToListAsync(ct);

        return candidates
            .Where(candidate => !handledKeys.Contains(candidate.DeduplicationKey, StringComparer.Ordinal))
            .OrderBy(candidate => candidate.SortAtUtc)
            .ThenBy(candidate => candidate.Title)
            .Select(MapCandidate)
            .ToList();
    }

    private async Task<List<RecurringTaskDto>> GetProcessedTasksAsync(RecurringTaskType? filterType, RecurringTaskStatus status, CancellationToken ct)
    {
        var query = db.RecurringTaskExecutions
            .AsNoTracking()
            .Include(execution => execution.Rule)
            .Include(execution => execution.Client)
            .ThenInclude(client => client!.Contacts)
            .Include(execution => execution.Teacher)
            .Include(execution => execution.Appointment)
            .Where(execution => execution.Status == status);

        if (filterType is { } type)
        {
            query = query.Where(execution => execution.Rule.Type == type);
        }

        var executions = await query
            .OrderByDescending(execution => execution.CompletedAtUtc ?? execution.SkippedAtUtc ?? execution.CreatedAtUtc)
            .ToListAsync(ct);

        return executions
            .Select(MapExecution)
            .ToList();
    }

    public async Task<RecurringTaskActionResult> CompleteAsync(CompleteRecurringTaskRequest request, User actor, CancellationToken ct)
    {
        if (!RecurringTaskTypeExtensions.TryParseApiKey(request.Type, out _))
        {
            return RecurringTaskActionResult.Failure("Неизвестный тип задачи.");
        }

        var existingExecution = await db.RecurringTaskExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(execution => execution.DeduplicationKey == request.DeduplicationKey, ct);

        if (existingExecution is not null)
        {
            return RecurringTaskActionResult.Failure("Задача уже обработана другим пользователем.");
        }

        var candidate = await FindCandidateAsync(request.Timezone, request.RuleId, request.DeduplicationKey, request.Type, request.ClientId, request.TeacherId, request.AppointmentId, ct);
        if (candidate is null)
        {
            return RecurringTaskActionResult.Failure("Задача больше не актуальна.");
        }

        var nowUtc = DateTime.UtcNow;
        var execution = new RecurringTaskExecution
        {
            Id = Ulid.NewUlid(),
            RuleId = candidate.RuleId,
            Rule = null!,
            Status = RecurringTaskStatus.Completed,
            RecipientType = candidate.RecipientType,
            ClientId = candidate.ClientId,
            TeacherId = candidate.TeacherId,
            AppointmentId = candidate.AppointmentId,
            BusinessDate = candidate.BusinessDate,
            DeduplicationKey = candidate.DeduplicationKey,
            GeneratedText = request.PreparedMessage ?? candidate.PreparedMessage,
            CompletedByUserId = actor.Id,
            SkippedByUserId = null,
            CreatedAtUtc = nowUtc,
            CompletedAtUtc = nowUtc,
            SkippedAtUtc = null
        };

        await db.RecurringTaskExecutions.AddAsync(execution, ct);
        await db.SaveChangesAsync(ct);

        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "recurring_tasks",
            Action = "task_completed",
            EntityType = "recurring_task",
            EntityId = execution.Id.ToString(),
            Details = $"Тип: {candidate.Type.ToApiKey()}; Ключ: {candidate.DeduplicationKey}; Получатель: {candidate.RelatedPersonDisplayName}"
        }, ct);

        return RecurringTaskActionResult.Success(RecurringTaskStatus.Completed);
    }

    public async Task<RecurringTaskActionResult> SkipAsync(SkipRecurringTaskRequest request, User actor, CancellationToken ct)
    {
        if (!RecurringTaskTypeExtensions.TryParseApiKey(request.Type, out _))
        {
            return RecurringTaskActionResult.Failure("Неизвестный тип задачи.");
        }

        var existingExecution = await db.RecurringTaskExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(execution => execution.DeduplicationKey == request.DeduplicationKey, ct);

        if (existingExecution is not null)
        {
            return RecurringTaskActionResult.Failure("Задача уже обработана другим пользователем.");
        }

        var candidate = await FindCandidateAsync(request.Timezone, request.RuleId, request.DeduplicationKey, request.Type, request.ClientId, request.TeacherId, request.AppointmentId, ct);
        if (candidate is null)
        {
            return RecurringTaskActionResult.Failure("Задача больше не актуальна.");
        }

        var nowUtc = DateTime.UtcNow;
        var execution = new RecurringTaskExecution
        {
            Id = Ulid.NewUlid(),
            RuleId = candidate.RuleId,
            Rule = null!,
            Status = RecurringTaskStatus.Skipped,
            RecipientType = candidate.RecipientType,
            ClientId = candidate.ClientId,
            TeacherId = candidate.TeacherId,
            AppointmentId = candidate.AppointmentId,
            BusinessDate = candidate.BusinessDate,
            DeduplicationKey = candidate.DeduplicationKey,
            GeneratedText = candidate.PreparedMessage,
            CompletedByUserId = null,
            SkippedByUserId = actor.Id,
            CreatedAtUtc = nowUtc,
            CompletedAtUtc = null,
            SkippedAtUtc = nowUtc
        };

        await db.RecurringTaskExecutions.AddAsync(execution, ct);
        await db.SaveChangesAsync(ct);

        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "recurring_tasks",
            Action = "task_skipped",
            EntityType = "recurring_task",
            EntityId = execution.Id.ToString(),
            Details = $"Тип: {candidate.Type.ToApiKey()}; Ключ: {candidate.DeduplicationKey}; Получатель: {candidate.RelatedPersonDisplayName}"
        }, ct);

        return RecurringTaskActionResult.Success(RecurringTaskStatus.Skipped);
    }

    private async Task<RecurringTaskCandidate?> FindCandidateAsync(
        string timezone,
        Ulid ruleId,
        string deduplicationKey,
        string typeApiKey,
        Ulid? clientId,
        Ulid? teacherId,
        Ulid? appointmentId,
        CancellationToken ct)
    {
        if (!RecurringTaskTypeExtensions.TryParseApiKey(typeApiKey, out var filterType))
        {
            return null;
        }

        var candidates = await GetOpenTasksAsync(timezone, filterType, ct);

        return candidates
            .Where(candidate => candidate.RuleId == ruleId
                                && candidate.DeduplicationKey == deduplicationKey
                                && candidate.ClientId == clientId
                                && candidate.TeacherId == teacherId
                                && candidate.AppointmentId == appointmentId)
            .Select(dto => new RecurringTaskCandidate
            {
                RuleId = dto.RuleId,
                Type = filterType,
                RecipientType = dto.RecipientType == "teacher" ? RecurringTaskRecipientType.Teacher : RecurringTaskRecipientType.Client,
                DeduplicationKey = dto.DeduplicationKey,
                ClientId = dto.ClientId,
                TeacherId = dto.TeacherId,
                AppointmentId = dto.AppointmentId,
                Title = dto.Title,
                RelatedPersonDisplayName = dto.RelatedPersonDisplayName,
                RelevantAtUtc = dto.RelevantAtUtc,
                BusinessDate = dto.BusinessDate,
                Phone = dto.Phone,
                Telegram = dto.Telegram,
                Vk = dto.Vk,
                PreparedMessage = dto.PreparedMessage,
                SortAtUtc = dto.RelevantAtUtc ?? DateTime.UtcNow
            })
            .FirstOrDefault();
    }

    private async Task<List<RecurringTaskCandidate>> BuildAppointmentReminderCandidatesAsync(RecurringTaskRule rule, string timezone, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var offsetMinutes = rule.OffsetMinutes ?? 24 * 60;
        var windowEndUtc = nowUtc.AddMinutes(offsetMinutes);

        var appointments = await db.Appointments
            .AsNoTracking()
            .Include(appointment => appointment.Client)
            .ThenInclude(client => client.Contacts)
            .Where(appointment =>
                !appointment.IsDeleted
                && appointment.Status == AppointmentStatus.Planned
                && appointment.StartDate >= nowUtc
                && appointment.StartDate <= windowEndUtc)
            .OrderBy(appointment => appointment.StartDate)
            .ToListAsync(ct);

        var candidates = new List<RecurringTaskCandidate>();

        foreach (var appointment in appointments)
        {
            if (!HasAnyClientContact(appointment.Client))
            {
                continue;
            }

            var localAppointmentDate = DateTimeUtils.ConvertDateToTimezone(appointment.StartDate, timezone);
            var localNow = DateTimeUtils.ConvertDateToTimezone(nowUtc, timezone);
            var whenWord = localAppointmentDate.Date == localNow.Date
                ? "сегодня"
                : localAppointmentDate.Date == localNow.Date.AddDays(1)
                    ? "завтра"
                    : localAppointmentDate.ToString("dd.MM.yyyy");

            candidates.Add(new RecurringTaskCandidate
            {
                RuleId = rule.Id,
                Type = RecurringTaskType.AppointmentReminder,
                RecipientType = RecurringTaskRecipientType.Client,
                DeduplicationKey = BuildAppointmentReminderDeduplicationKey(rule.Id, appointment.Id, appointment.StartDate, offsetMinutes),
                ClientId = appointment.Client.Id,
                AppointmentId = appointment.Id,
                Title = "Напомнить о записи",
                RelatedPersonDisplayName = FormatClientName(appointment.Client),
                RelevantAtUtc = appointment.StartDate,
                BusinessDate = DateOnly.FromDateTime(localAppointmentDate),
                Phone = appointment.Client.Contacts.Phone,
                Telegram = appointment.Client.Contacts.Telegram,
                Vk = appointment.Client.Contacts.Vk,
                PreparedMessage = RenderMessageTemplate(
                    rule.MessageTemplate,
                    clientFirstName: appointment.Client.FirstName,
                    clientLastName: appointment.Client.LastName,
                    clientPatronymic: appointment.Client.Patronymic,
                    whenWord: whenWord,
                    appointmentStartTime: localAppointmentDate.ToString("HH:mm"),
                    appointmentDate: localAppointmentDate.ToString("dd.MM.yyyy")),
                SortAtUtc = appointment.StartDate
            });
        }

        return candidates;
    }

    private async Task<List<RecurringTaskCandidate>> BuildBirthdayCandidatesAsync(RecurringTaskRule rule, string timezone, CancellationToken ct)
    {
        var todayLocal = DateOnly.FromDateTime(DateTimeUtils.ConvertDateToTimezone(DateTime.UtcNow, timezone));

        var clients = await db.Clients
            .AsNoTracking()
            .Include(client => client.Contacts)
            .Where(client => client.DateOfBirth != null)
            .OrderBy(client => client.LastName)
            .ThenBy(client => client.FirstName)
            .ToListAsync(ct);

        return clients
            .Where(client =>
                client.DateOfBirth?.Day == todayLocal.Day
                && client.DateOfBirth?.Month == todayLocal.Month
                && HasAnyClientContact(client))
            .Select(client => new RecurringTaskCandidate
            {
                RuleId = rule.Id,
                Type = RecurringTaskType.BirthdayGreeting,
                RecipientType = RecurringTaskRecipientType.Client,
                DeduplicationKey = $"birthday:{rule.Id}:{client.Id}:{todayLocal.Year}",
                ClientId = client.Id,
                AppointmentId = null,
                Title = "Поздравить с днём рождения",
                RelatedPersonDisplayName = FormatClientName(client),
                RelevantAtUtc = null,
                BusinessDate = todayLocal,
                Phone = client.Contacts.Phone,
                Telegram = client.Contacts.Telegram,
                Vk = client.Contacts.Vk,
                PreparedMessage = RenderMessageTemplate(
                    rule.MessageTemplate,
                    clientFirstName: client.FirstName,
                    clientLastName: client.LastName,
                    clientPatronymic: client.Patronymic,
                    date: todayLocal.ToString("dd.MM.yyyy")),
                SortAtUtc = DateTime.UtcNow
            })
            .ToList();
    }

    private async Task<List<RecurringTaskCandidate>> BuildTrialFollowUpCandidatesAsync(RecurringTaskRule rule, string timezone, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var followUpAfterMinutes = rule.OffsetMinutes ?? 24 * 60;
        var latestAllowedStartUtc = nowUtc.AddMinutes(-followUpAfterMinutes);

        var appointments = await db.Appointments
            .AsNoTracking()
            .Include(appointment => appointment.Client)
            .ThenInclude(client => client.Contacts)
            .Include(appointment => appointment.Service)
            .Where(appointment =>
                !appointment.IsDeleted
                && appointment.Status == AppointmentStatus.Completed
                && appointment.StartDate <= latestAllowedStartUtc)
            .OrderBy(appointment => appointment.StartDate)
            .ToListAsync(ct);

        if (appointments.Count == 0)
        {
            return [];
        }

        var serviceIds = appointments
            .Select(appointment => appointment.Service.Id)
            .Distinct()
            .ToList();

        var clientIds = appointments
            .Select(appointment => appointment.Client.Id)
            .Distinct()
            .ToList();

        var priceHistory = await db.ServicePriceHistory
            .AsNoTracking()
            .Include(entry => entry.Service)
            .Where(entry => serviceIds.Contains(entry.Service.Id))
            .OrderBy(entry => entry.EffectiveDate)
            .ToListAsync(ct);

        var futurePlannedAppointments = await db.Appointments
            .AsNoTracking()
            .Where(appointment =>
                !appointment.IsDeleted
                && appointment.Status == AppointmentStatus.Planned
                && clientIds.Contains(appointment.Client.Id))
            .Select(appointment => new
            {
                ClientId = appointment.Client.Id,
                appointment.StartDate
            })
            .ToListAsync(ct);

        var pricesByServiceId = priceHistory
            .GroupBy(entry => entry.Service.Id)
            .ToDictionary(group => group.Key, group => group.OrderBy(entry => entry.EffectiveDate).ToList());

        var plannedByClientId = futurePlannedAppointments
            .GroupBy(appointment => appointment.ClientId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.StartDate).OrderBy(date => date).ToList());

        var candidates = new List<RecurringTaskCandidate>();

        foreach (var appointment in appointments)
        {
            if (!HasAnyClientContact(appointment.Client))
            {
                continue;
            }

            var serviceName = appointment.Service.Name.ToLowerInvariant();
            var serviceDescription = appointment.Service.Description?.ToLowerInvariant();
            if (!serviceName.Contains("проб", StringComparison.Ordinal)
                && !(serviceDescription?.Contains("проб", StringComparison.Ordinal) ?? false))
            {
                continue;
            }

            if (!pricesByServiceId.TryGetValue(appointment.Service.Id, out var servicePrices))
            {
                continue;
            }

            var effectivePrice = servicePrices
                .Where(entry => entry.EffectiveDate <= appointment.StartDate)
                .OrderByDescending(entry => entry.EffectiveDate)
                .FirstOrDefault();

            if (effectivePrice is null || effectivePrice.Price != 0)
            {
                continue;
            }

            if (plannedByClientId.TryGetValue(appointment.Client.Id, out var clientPlannedDates)
                && clientPlannedDates.Any(date => date > appointment.StartDate))
            {
                continue;
            }

            var businessDate = DateOnly.FromDateTime(DateTimeUtils.ConvertDateToTimezone(nowUtc, timezone));

            candidates.Add(new RecurringTaskCandidate
            {
                RuleId = rule.Id,
                Type = RecurringTaskType.TrialFollowUp,
                RecipientType = RecurringTaskRecipientType.Client,
                DeduplicationKey = $"trial-follow-up:{rule.Id}:{appointment.Id}",
                ClientId = appointment.Client.Id,
                AppointmentId = appointment.Id,
                Title = "Связаться после пробного занятия",
                RelatedPersonDisplayName = FormatClientName(appointment.Client),
                RelevantAtUtc = appointment.StartDate,
                BusinessDate = businessDate,
                Phone = appointment.Client.Contacts.Phone,
                Telegram = appointment.Client.Contacts.Telegram,
                Vk = appointment.Client.Contacts.Vk,
                PreparedMessage = RenderMessageTemplate(
                    rule.MessageTemplate,
                    clientFirstName: appointment.Client.FirstName,
                    clientLastName: appointment.Client.LastName,
                    clientPatronymic: appointment.Client.Patronymic,
                    date: businessDate.ToString("dd.MM.yyyy")),
                SortAtUtc = appointment.StartDate
            });
        }

        return candidates;
    }

    private async Task<List<RecurringTaskCandidate>> BuildInactiveClientCandidatesAsync(RecurringTaskRule rule, string timezone, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var todayLocal = DateOnly.FromDateTime(DateTimeUtils.ConvertDateToTimezone(nowUtc, timezone));
        var cooldownDays = rule.CooldownDays ?? 7;

        var attendedAppointments = await db.Appointments
            .AsNoTracking()
            .Include(appointment => appointment.Client)
            .ThenInclude(client => client.Contacts)
            .Where(appointment =>
                !appointment.IsDeleted
                && (appointment.Status == AppointmentStatus.Completed || appointment.Status == AppointmentStatus.Burned))
            .Select(appointment => new
            {
                Client = appointment.Client,
                appointment.StartDate
            })
            .ToListAsync(ct);

        if (attendedAppointments.Count == 0)
        {
            return [];
        }

        var clientIds = attendedAppointments
            .Select(appointment => appointment.Client.Id)
            .Distinct()
            .ToList();

        var futurePlannedClientIds = await db.Appointments
            .AsNoTracking()
            .Where(appointment =>
                !appointment.IsDeleted
                && appointment.Status == AppointmentStatus.Planned
                && appointment.StartDate > nowUtc
                && clientIds.Contains(appointment.Client.Id))
            .Select(appointment => appointment.Client.Id)
            .Distinct()
            .ToListAsync(ct);

        var futurePlannedLookup = futurePlannedClientIds.ToHashSet();

        var lastAttendanceByClient = attendedAppointments
            .GroupBy(appointment => appointment.Client.Id)
            .Select(group => group.OrderByDescending(item => item.StartDate).First())
            .ToList();

        var candidates = new List<RecurringTaskCandidate>();

        foreach (var attendance in lastAttendanceByClient)
        {
            if (!HasAnyClientContact(attendance.Client))
            {
                continue;
            }

            if (futurePlannedLookup.Contains(attendance.Client.Id))
            {
                continue;
            }

            var lastAttendanceLocalDate = DateOnly.FromDateTime(DateTimeUtils.ConvertDateToTimezone(attendance.StartDate, timezone));
            var daysSinceLastAttendance = todayLocal.DayNumber - lastAttendanceLocalDate.DayNumber;
            if (daysSinceLastAttendance < 7)
            {
                continue;
            }

            var periodsSinceThreshold = (daysSinceLastAttendance - 7) / cooldownDays;
            var periodStartDate = lastAttendanceLocalDate.AddDays(7 + periodsSinceThreshold * cooldownDays);

            candidates.Add(new RecurringTaskCandidate
            {
                RuleId = rule.Id,
                Type = RecurringTaskType.InactiveClientReminder,
                RecipientType = RecurringTaskRecipientType.Client,
                DeduplicationKey = $"inactive-client:{rule.Id}:{attendance.Client.Id}:{periodStartDate:yyyy-MM-dd}",
                ClientId = attendance.Client.Id,
                AppointmentId = null,
                Title = "Напомнить о занятиях",
                RelatedPersonDisplayName = FormatClientName(attendance.Client),
                RelevantAtUtc = attendance.StartDate,
                BusinessDate = periodStartDate,
                Phone = attendance.Client.Contacts.Phone,
                Telegram = attendance.Client.Contacts.Telegram,
                Vk = attendance.Client.Contacts.Vk,
                PreparedMessage = RenderMessageTemplate(
                    rule.MessageTemplate,
                    clientFirstName: attendance.Client.FirstName,
                    clientLastName: attendance.Client.LastName,
                    clientPatronymic: attendance.Client.Patronymic,
                    date: periodStartDate.ToString("dd.MM.yyyy")),
                SortAtUtc = attendance.StartDate
            });
        }

        return candidates;
    }

    private async Task<List<RecurringTaskCandidate>> BuildTeacherDailyScheduleCandidatesAsync(RecurringTaskRule rule, string timezone, CancellationToken ct)
    {
        var todayLocal = DateOnly.FromDateTime(DateTimeUtils.ConvertDateToTimezone(DateTime.UtcNow, timezone));
        var dayStartUtc = DateTimeUtils.ConvertLocalDateToUtc(todayLocal, TimeOnly.MinValue, timezone);
        var nextDayStartUtc = DateTimeUtils.ConvertLocalDateToUtc(todayLocal.AddDays(1), TimeOnly.MinValue, timezone);

        var teacherAppointments = await db.Appointments
            .AsNoTracking()
            .Where(appointment =>
                !appointment.IsDeleted
                && appointment.Provider != null
                && appointment.Status == AppointmentStatus.Planned
                && appointment.StartDate >= dayStartUtc
                && appointment.StartDate < nextDayStartUtc)
            .Select(appointment => new
            {
                TeacherId = appointment.Provider!.Id,
                appointment.Provider!.FirstName,
                appointment.Provider.LastName,
                appointment.Provider.Phone,
                appointment.Provider.Telegram,
                appointment.Provider.Vk,
                appointment.StartDate
            })
            .ToListAsync(ct);

        return teacherAppointments
            .GroupBy(appointment => new
            {
                appointment.TeacherId,
                appointment.FirstName,
                appointment.LastName,
                appointment.Phone,
                appointment.Telegram,
                appointment.Vk
            })
            .Where(group =>
                !string.IsNullOrWhiteSpace(group.Key.Phone)
                || !string.IsNullOrWhiteSpace(group.Key.Telegram)
                || !string.IsNullOrWhiteSpace(group.Key.Vk))
            .Select(group => new RecurringTaskCandidate
            {
                RuleId = rule.Id,
                Type = RecurringTaskType.TeacherDailySchedule,
                RecipientType = RecurringTaskRecipientType.Teacher,
                DeduplicationKey = $"teacher-schedule:{rule.Id}:{group.Key.TeacherId}:{todayLocal:yyyy-MM-dd}",
                ClientId = null,
                TeacherId = group.Key.TeacherId,
                AppointmentId = null,
                Title = "Отправить расписание",
                RelatedPersonDisplayName = $"{group.Key.LastName} {group.Key.FirstName}".Trim(),
                RelevantAtUtc = dayStartUtc,
                BusinessDate = todayLocal,
                Phone = group.Key.Phone,
                Telegram = group.Key.Telegram,
                Vk = group.Key.Vk,
                PreparedMessage = RenderMessageTemplate(
                    rule.MessageTemplate,
                    teacherFirstName: group.Key.FirstName,
                    teacherLastName: group.Key.LastName,
                    date: todayLocal.ToString("dd.MM.yyyy")),
                SortAtUtc = group.Min(item => item.StartDate)
            })
            .OrderBy(candidate => candidate.RelatedPersonDisplayName)
            .ToList();
    }

    private static string RenderMessageTemplate(
        string template,
        string? clientFirstName = null,
        string? clientLastName = null,
        string? clientPatronymic = null,
        string? teacherFirstName = null,
        string? teacherLastName = null,
        string? whenWord = null,
        string? appointmentStartTime = null,
        string? appointmentDate = null,
        string? date = null)
    {
        return template
            .Replace("{Client.FirstName}", clientFirstName ?? string.Empty, StringComparison.Ordinal)
            .Replace("{Client.LastName}", clientLastName ?? string.Empty, StringComparison.Ordinal)
            .Replace("{Client.Patronymic}", clientPatronymic ?? string.Empty, StringComparison.Ordinal)
            .Replace("{Teacher.FirstName}", teacherFirstName ?? string.Empty, StringComparison.Ordinal)
            .Replace("{Teacher.LastName}", teacherLastName ?? string.Empty, StringComparison.Ordinal)
            .Replace("{When}", whenWord ?? string.Empty, StringComparison.Ordinal)
            .Replace("{Appointment.StartTime}", appointmentStartTime ?? string.Empty, StringComparison.Ordinal)
            .Replace("{Appointment.Date}", appointmentDate ?? string.Empty, StringComparison.Ordinal)
            .Replace("{Date}", date ?? appointmentDate ?? string.Empty, StringComparison.Ordinal);
    }

    private static bool HasAnyClientContact(Client client)
    {
        return !string.IsNullOrWhiteSpace(client.Contacts.Phone)
               || !string.IsNullOrWhiteSpace(client.Contacts.Telegram)
               || !string.IsNullOrWhiteSpace(client.Contacts.Vk);
    }

    private static string FormatClientName(Client? client)
    {
        if (client is null)
        {
            return "Клиент";
        }

        return string.Join(' ', new[] { client.LastName, client.FirstName, client.Patronymic }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string BuildAppointmentReminderDeduplicationKey(Ulid ruleId, Ulid appointmentId, DateTime appointmentStartUtc, int offsetMinutes)
    {
        var normalizedStartUtc = appointmentStartUtc.Kind == DateTimeKind.Utc
            ? appointmentStartUtc
            : DateTime.SpecifyKind(appointmentStartUtc, DateTimeKind.Utc);

        return $"appointment-reminder:{ruleId}:{appointmentId}:{normalizedStartUtc:yyyyMMddHHmmss}:{offsetMinutes}";
    }

    private static RecurringTaskDto MapCandidate(RecurringTaskCandidate candidate)
    {
        return new RecurringTaskDto
        {
            RuleId = candidate.RuleId,
            Type = candidate.Type.ToApiKey(),
            RecipientType = candidate.RecipientType == RecurringTaskRecipientType.Teacher ? "teacher" : "client",
            DeduplicationKey = candidate.DeduplicationKey,
            ClientId = candidate.ClientId,
            TeacherId = candidate.TeacherId,
            AppointmentId = candidate.AppointmentId,
            Title = candidate.Title,
            RelatedPersonDisplayName = candidate.RelatedPersonDisplayName,
            RelevantAtUtc = candidate.RelevantAtUtc,
            BusinessDate = candidate.BusinessDate,
            Phone = candidate.Phone,
            Telegram = candidate.Telegram,
            Vk = candidate.Vk,
            PreparedMessage = candidate.PreparedMessage
        };
    }

    private static RecurringTaskDto MapExecution(RecurringTaskExecution execution)
    {
        var type = execution.Rule.Type;
        var relatedPersonDisplayName = execution.RecipientType == RecurringTaskRecipientType.Teacher
            ? FormatTeacherName(execution.Teacher)
            : FormatClientName(execution.Client);

        return new RecurringTaskDto
        {
            RuleId = execution.RuleId,
            Type = type.ToApiKey(),
            RecipientType = execution.RecipientType == RecurringTaskRecipientType.Teacher ? "teacher" : "client",
            DeduplicationKey = execution.DeduplicationKey,
            ClientId = execution.ClientId,
            TeacherId = execution.TeacherId,
            AppointmentId = execution.AppointmentId,
            Title = GetTaskTitle(type),
            RelatedPersonDisplayName = relatedPersonDisplayName,
            RelevantAtUtc = execution.Appointment?.StartDate,
            BusinessDate = execution.BusinessDate,
            Phone = execution.RecipientType == RecurringTaskRecipientType.Teacher ? execution.Teacher?.Phone : execution.Client?.Contacts.Phone,
            Telegram = execution.RecipientType == RecurringTaskRecipientType.Teacher ? execution.Teacher?.Telegram : execution.Client?.Contacts.Telegram,
            Vk = execution.RecipientType == RecurringTaskRecipientType.Teacher ? execution.Teacher?.Vk : execution.Client?.Contacts.Vk,
            PreparedMessage = execution.GeneratedText ?? string.Empty
        };
    }

    private static string GetTaskTitle(RecurringTaskType type)
    {
        return type switch
        {
            RecurringTaskType.AppointmentReminder => "Напомнить о записи",
            RecurringTaskType.BirthdayGreeting => "Поздравить с днём рождения",
            RecurringTaskType.TrialFollowUp => "Связаться после пробного занятия",
            RecurringTaskType.InactiveClientReminder => "Напомнить о занятиях",
            RecurringTaskType.TeacherDailySchedule => "Отправить расписание",
            _ => "Задача"
        };
    }

    private static string FormatTeacherName(User? teacher)
    {
        if (teacher is null)
        {
            return "Преподаватель";
        }

        return string.Join(' ', new[] { teacher.LastName, teacher.FirstName }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private sealed class RecurringTaskCandidate
    {
        public required Ulid RuleId { get; init; }
        public required RecurringTaskType Type { get; init; }
        public required RecurringTaskRecipientType RecipientType { get; init; }
        public required string DeduplicationKey { get; init; }
        public Ulid? ClientId { get; init; }
        public Ulid? TeacherId { get; init; }
        public Ulid? AppointmentId { get; init; }
        public required string Title { get; init; }
        public required string RelatedPersonDisplayName { get; init; }
        public DateTime? RelevantAtUtc { get; init; }
        public required DateOnly BusinessDate { get; init; }
        public string? Phone { get; init; }
        public string? Telegram { get; init; }
        public string? Vk { get; init; }
        public required string PreparedMessage { get; init; }
        public required DateTime SortAtUtc { get; init; }
    }
}
