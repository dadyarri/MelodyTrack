using MelodyTrack.Backend.Api.Tasks.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services.RecurringTasks;

internal interface IRecurringTaskCandidateService
{
    Task<List<RecurringTaskDto>> GetOpenTasksAsync(
        string timezone,
        RecurringTaskType? filterType,
        CancellationToken ct);

    Task<RecurringTaskCandidate?> FindCandidateAsync(
        string timezone,
        Ulid ruleId,
        string deduplicationKey,
        string typeApiKey,
        Ulid? clientId,
        Ulid? teacherId,
        Ulid? appointmentId,
        CancellationToken ct);
}

internal sealed class RecurringTaskCandidateService(
    AppDbContext db,
    TimeProvider timeProvider,
    IRecurringTaskTemplateRenderer templateRenderer) : IRecurringTaskCandidateService
{
    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;


    public async Task<List<RecurringTaskDto>> GetOpenTasksAsync(
        string timezone,
        RecurringTaskType? filterType,
        CancellationToken ct)
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

        if (filterType is null or RecurringTaskType.CustomTask)
        {
            candidates.AddRange(await BuildCustomTaskCandidatesAsync(timezone, ct));
        }

        foreach (var rule in rules.Where(rule => rule.Type != RecurringTaskType.DebtorReminder))
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

        var debtorRules = rules
            .Where(rule => rule.Type == RecurringTaskType.DebtorReminder)
            .ToList();
        if (debtorRules.Count > 0)
        {
            var debtorData = await LoadDebtorReminderDataAsync(debtorRules, timezone, ct);
            foreach (var rule in debtorRules)
            {
                candidates.AddRange(BuildDebtorReminderCandidates(rule, timezone, debtorData));
            }
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        candidates = await ExcludeClientVacationCandidatesAsync(candidates, ct);
        if (candidates.Count == 0)
        {
            return [];
        }

        var deduplicationKeys = candidates
            .Select(candidate => candidate.DeduplicationKey)
            .Distinct()
            .ToList();

        var nowUtc = UtcNow;
        var handledKeys = await db.RecurringTaskExecutions
            .AsNoTracking()
            .Where(execution => deduplicationKeys.Contains(execution.DeduplicationKey))
            .Where(execution => execution.Status != RecurringTaskStatus.Delayed || execution.DelayedUntilUtc == null || execution.DelayedUntilUtc > nowUtc)
            .Select(execution => execution.DeduplicationKey)
            .ToListAsync(ct);

        return candidates
            .Where(candidate => !handledKeys.Contains(candidate.DeduplicationKey, StringComparer.Ordinal))
            .OrderBy(candidate => candidate.SortAtUtc)
            .ThenBy(candidate => candidate.Title)
            .Select(RecurringTaskPresentationMapper.MapCandidate)
            .ToList();
    }


    public async Task<RecurringTaskCandidate?> FindCandidateAsync(
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
                RecipientType = dto.RecipientType switch
                {
                    "teacher" => RecurringTaskRecipientType.Teacher,
                    "external" => RecurringTaskRecipientType.External,
                    _ => RecurringTaskRecipientType.Client
                },
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
                SortAtUtc = dto.RelevantAtUtc ?? UtcNow
            })
            .FirstOrDefault();
    }

    private async Task<List<RecurringTaskCandidate>> BuildAppointmentReminderCandidatesAsync(RecurringTaskRule rule, string timezone, CancellationToken ct)
    {
        var nowUtc = UtcNow;
        var offsetMinutes = rule.OffsetMinutes ?? 24 * 60;
        var windowEndUtc = nowUtc.AddMinutes(offsetMinutes);

        var appointments = await db.Appointments
            .AsNoTracking()
            .Include(appointment => appointment.Client)
            .ThenInclude(client => client.Contacts)
            .Include(appointment => appointment.Service)
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
                RelatedPersonDisplayName = RecurringTaskPresentationMapper.FormatClientName(appointment.Client),
                RelevantAtUtc = appointment.StartDate,
                BusinessDate = DateOnly.FromDateTime(localAppointmentDate),
                Phone = appointment.Client.Contacts.Phone,
                Telegram = appointment.Client.Contacts.Telegram,
                Vk = appointment.Client.Contacts.Vk,
                PreparedMessage = templateRenderer.Render(
                    rule.MessageTemplate,
                    new RecurringTaskTemplateValues
                    {
                        ClientFirstName = appointment.Client.FirstName,
                        ClientLastName = appointment.Client.LastName,
                        ClientPatronymic = appointment.Client.Patronymic,
                        WhenWord = whenWord,
                        AppointmentStartTime = localAppointmentDate.ToString("HH:mm"),
                        AppointmentDate = localAppointmentDate.ToString("dd.MM.yyyy")
                    }),
                SortAtUtc = appointment.StartDate
            });
        }

        return candidates;
    }

    private async Task<List<RecurringTaskCandidate>> BuildBirthdayCandidatesAsync(RecurringTaskRule rule, string timezone, CancellationToken ct)
    {
        var todayLocal = DateOnly.FromDateTime(DateTimeUtils.ConvertDateToTimezone(UtcNow, timezone));

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
                RelatedPersonDisplayName = RecurringTaskPresentationMapper.FormatClientName(client),
                RelevantAtUtc = null,
                BusinessDate = todayLocal,
                Phone = client.Contacts.Phone,
                Telegram = client.Contacts.Telegram,
                Vk = client.Contacts.Vk,
                PreparedMessage = templateRenderer.Render(
                    rule.MessageTemplate,
                    new RecurringTaskTemplateValues
                    {
                        ClientFirstName = client.FirstName,
                        ClientLastName = client.LastName,
                        ClientPatronymic = client.Patronymic,
                        Date = todayLocal.ToString("dd.MM.yyyy")
                    }),
                SortAtUtc = UtcNow
            })
            .ToList();
    }

    private async Task<List<RecurringTaskCandidate>> BuildTrialFollowUpCandidatesAsync(RecurringTaskRule rule, string timezone, CancellationToken ct)
    {
        var nowUtc = UtcNow;
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

        var laterAttendedAppointments = await db.Appointments
            .AsNoTracking()
            .Where(appointment =>
                !appointment.IsDeleted
                && (appointment.Status == AppointmentStatus.Completed || appointment.Status == AppointmentStatus.Burned)
                && clientIds.Contains(appointment.Client.Id))
            .Select(appointment => new
            {
                ClientId = appointment.Client.Id,
                ServiceId = appointment.Service.Id,
                appointment.StartDate
            })
            .ToListAsync(ct);

        var priceServiceIds = serviceIds
            .Concat(laterAttendedAppointments.Select(appointment => appointment.ServiceId))
            .Distinct()
            .ToList();

        var priceHistory = await db.ServicePriceHistory
            .AsNoTracking()
            .Include(entry => entry.Service)
            .Where(entry => priceServiceIds.Contains(entry.Service.Id))
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

            var hasPaidAppointmentAfterTrial = laterAttendedAppointments.Any(laterAppointment =>
                laterAppointment.ClientId == appointment.Client.Id
                && laterAppointment.StartDate > appointment.StartDate
                && pricesByServiceId.TryGetValue(laterAppointment.ServiceId, out var laterServicePrices)
                && laterServicePrices
                    .Where(entry => entry.EffectiveDate <= laterAppointment.StartDate)
                    .OrderByDescending(entry => entry.EffectiveDate)
                    .FirstOrDefault() is { Price: > 0 });

            if (hasPaidAppointmentAfterTrial)
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
                RelatedPersonDisplayName = RecurringTaskPresentationMapper.FormatClientName(appointment.Client),
                RelevantAtUtc = appointment.StartDate,
                BusinessDate = businessDate,
                Phone = appointment.Client.Contacts.Phone,
                Telegram = appointment.Client.Contacts.Telegram,
                Vk = appointment.Client.Contacts.Vk,
                PreparedMessage = templateRenderer.Render(
                    rule.MessageTemplate,
                    new RecurringTaskTemplateValues
                    {
                        ClientFirstName = appointment.Client.FirstName,
                        ClientLastName = appointment.Client.LastName,
                        ClientPatronymic = appointment.Client.Patronymic,
                        Date = businessDate.ToString("dd.MM.yyyy")
                    }),
                SortAtUtc = appointment.StartDate
            });
        }

        return candidates;
    }

    private async Task<List<RecurringTaskCandidate>> BuildInactiveClientCandidatesAsync(RecurringTaskRule rule, string timezone, CancellationToken ct)
    {
        var nowUtc = UtcNow;
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
                RelatedPersonDisplayName = RecurringTaskPresentationMapper.FormatClientName(attendance.Client),
                RelevantAtUtc = attendance.StartDate,
                BusinessDate = periodStartDate,
                Phone = attendance.Client.Contacts.Phone,
                Telegram = attendance.Client.Contacts.Telegram,
                Vk = attendance.Client.Contacts.Vk,
                PreparedMessage = templateRenderer.Render(
                    rule.MessageTemplate,
                    new RecurringTaskTemplateValues
                    {
                        ClientFirstName = attendance.Client.FirstName,
                        ClientLastName = attendance.Client.LastName,
                        ClientPatronymic = attendance.Client.Patronymic,
                        Date = periodStartDate.ToString("dd.MM.yyyy")
                    }),
                SortAtUtc = attendance.StartDate
            });
        }

        return candidates;
    }

    private async Task<List<RecurringTaskCandidate>> BuildTeacherDailyScheduleCandidatesAsync(RecurringTaskRule rule, string timezone, CancellationToken ct)
    {
        var todayLocal = DateOnly.FromDateTime(DateTimeUtils.ConvertDateToTimezone(UtcNow, timezone));
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
                PreparedMessage = templateRenderer.Render(
                    rule.MessageTemplate,
                    new RecurringTaskTemplateValues
                    {
                        TeacherFirstName = group.Key.FirstName,
                        TeacherLastName = group.Key.LastName,
                        Date = todayLocal.ToString("dd.MM.yyyy")
                    }),
                SortAtUtc = group.Min(item => item.StartDate)
            })
            .OrderBy(candidate => candidate.RelatedPersonDisplayName)
            .ToList();
    }

    private async Task<DebtorReminderData> LoadDebtorReminderDataAsync(
        IReadOnlyCollection<RecurringTaskRule> rules,
        string timezone,
        CancellationToken ct)
    {
        var todayLocal = DateOnly.FromDateTime(DateTimeUtils.ConvertDateToTimezone(UtcNow, timezone));
        var appointmentEntities = await db.Appointments
            .AsNoTracking()
            .Include(appointment => appointment.Client)
            .ThenInclude(client => client.Contacts)
            .Include(appointment => appointment.Service)
            .Where(appointment =>
                !appointment.IsDeleted
                && (appointment.Status == AppointmentStatus.Completed || appointment.Status == AppointmentStatus.Burned))
            .ToListAsync(ct);

        var appointments = appointmentEntities
            .Select(appointment => new DebtorAppointment(
                appointment.Id,
                appointment.Client,
                appointment.Service.Id,
                appointment.StartDate))
            .ToList();

        if (appointments.Count == 0)
        {
            return new DebtorReminderData(
                todayLocal,
                [],
                new Dictionary<Ulid, List<DebtorPayment>>(),
                new Dictionary<Ulid, List<DebtorServicePrice>>(),
                GetDebtorReminderStageStartDays(rules));
        }

        var clientIds = appointments
            .Select(appointment => appointment.Client.Id)
            .Distinct()
            .ToList();

        var serviceIds = appointments
            .Select(appointment => appointment.ServiceId)
            .Distinct()
            .ToList();

        var payments = await db.Payments
            .AsNoTracking()
            .Where(payment => clientIds.Contains(payment.Client.Id))
            .Select(payment => new DebtorPayment(payment.Client.Id, payment.Amount, payment.Date))
            .ToListAsync(ct);

        var priceHistory = await db.ServicePriceHistory
            .AsNoTracking()
            .Where(entry => serviceIds.Contains(entry.Service.Id))
            .Select(entry => new
            {
                ServiceId = entry.Service.Id,
                entry.EffectiveDate,
                entry.Price
            })
            .ToListAsync(ct);

        var priceLookup = priceHistory
            .GroupBy(entry => entry.ServiceId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(entry => entry.EffectiveDate)
                    .Select(entry => new DebtorServicePrice(entry.EffectiveDate, entry.Price))
                    .ToList());

        return new DebtorReminderData(
            todayLocal,
            appointments,
            payments
                .GroupBy(payment => payment.ClientId)
                .ToDictionary(group => group.Key, group => group.OrderBy(payment => payment.Date).ToList()),
            priceLookup,
            GetDebtorReminderStageStartDays(rules));
    }

    private List<RecurringTaskCandidate> BuildDebtorReminderCandidates(
        RecurringTaskRule rule,
        string timezone,
        DebtorReminderData data)
    {
        var initialDelayDays = Math.Max(1, (rule.OffsetMinutes ?? 24 * 60) / (24 * 60));
        var repeatEveryDays = rule.CooldownDays;
        var currentStageStartDays = GetDebtorReminderStageStartDays(initialDelayDays, repeatEveryDays);
        var nextStageStartDays = data.StageStartDays
            .Where(stageStartDays => stageStartDays > currentStageStartDays)
            .DefaultIfEmpty(int.MaxValue)
            .Min();

        var candidates = new List<RecurringTaskCandidate>();

        foreach (var clientAppointments in data.Appointments.GroupBy(appointment => appointment.Client.Id))
        {
            var client = clientAppointments.First().Client;
            if (!HasAnyClientContact(client))
            {
                continue;
            }

            var openLedgers = clientAppointments
                .OrderBy(appointment => appointment.StartDate)
                .Select(appointment => new DebtorAppointmentLedger
                {
                    AppointmentId = appointment.Id,
                    StartDate = appointment.StartDate,
                    Price = ResolveDebtorAppointmentPrice(appointment.ServiceId, appointment.StartDate, data.PriceLookup),
                    RemainingAmount = ResolveDebtorAppointmentPrice(appointment.ServiceId, appointment.StartDate, data.PriceLookup)
                })
                .Where(ledger => ledger.Price > 0)
                .ToList();

            if (openLedgers.Count == 0)
            {
                continue;
            }

            var remainingPayments = data.PaymentsByClient
                .GetValueOrDefault(clientAppointments.Key, [])
                .Select(payment => payment.Amount)
                .ToList();

            foreach (var paymentAmount in remainingPayments)
            {
                var remaining = paymentAmount;
                foreach (var ledger in openLedgers.Where(ledger => ledger.RemainingAmount > 0))
                {
                    if (remaining <= 0)
                    {
                        break;
                    }

                    var allocated = Math.Min(ledger.RemainingAmount, remaining);
                    ledger.RemainingAmount -= allocated;
                    remaining -= allocated;
                }
            }

            var firstOutstandingLedger = openLedgers
                .Where(ledger => ledger.RemainingAmount > 0)
                .OrderBy(ledger => ledger.StartDate)
                .FirstOrDefault();

            if (firstOutstandingLedger is null)
            {
                continue;
            }

            var debtAppearedDate = DateOnly.FromDateTime(DateTimeUtils.ConvertDateToTimezone(firstOutstandingLedger.StartDate, timezone));
            var debtAgeDays = data.TodayLocal.DayNumber - debtAppearedDate.DayNumber;

            DateOnly businessDate;
            if (repeatEveryDays is > 0)
            {
                if (debtAgeDays < currentStageStartDays)
                {
                    continue;
                }

                businessDate = debtAppearedDate.AddDays(currentStageStartDays + ((debtAgeDays - currentStageStartDays) / repeatEveryDays.Value) * repeatEveryDays.Value);
            }
            else
            {
                if (debtAgeDays < initialDelayDays || debtAgeDays >= nextStageStartDays)
                {
                    continue;
                }

                businessDate = debtAppearedDate.AddDays(initialDelayDays);
            }

            candidates.Add(new RecurringTaskCandidate
            {
                RuleId = rule.Id,
                Type = RecurringTaskType.DebtorReminder,
                RecipientType = RecurringTaskRecipientType.Client,
                DeduplicationKey = $"debtor-reminder:{rule.Id}:{client.Id}:{businessDate:yyyy-MM-dd}",
                ClientId = client.Id,
                AppointmentId = firstOutstandingLedger.AppointmentId,
                Title = "Напомнить о долге",
                RelatedPersonDisplayName = RecurringTaskPresentationMapper.FormatClientName(client),
                RelevantAtUtc = firstOutstandingLedger.StartDate,
                BusinessDate = businessDate,
                Phone = client.Contacts.Phone,
                Telegram = client.Contacts.Telegram,
                Vk = client.Contacts.Vk,
                PreparedMessage = templateRenderer.Render(
                    rule.MessageTemplate,
                    new RecurringTaskTemplateValues
                    {
                        ClientFirstName = client.FirstName,
                        ClientLastName = client.LastName,
                        ClientPatronymic = client.Patronymic,
                        Date = businessDate.ToString("dd.MM.yyyy")
                    }),
                SortAtUtc = firstOutstandingLedger.StartDate
            });
        }

        return candidates
            .OrderBy(candidate => candidate.BusinessDate)
            .ThenBy(candidate => candidate.RelatedPersonDisplayName)
            .ToList();
    }

    private async Task<List<RecurringTaskCandidate>> BuildCustomTaskCandidatesAsync(string timezone, CancellationToken ct)
    {
        var nowUtc = UtcNow;
        var tasks = await db.CustomTasks
            .AsNoTracking()
            .Include(item => item.Client)
            .ThenInclude(client => client!.Contacts)
            .Where(item =>
                item.CompletedAtUtc == null
                && item.CancelledAtUtc == null
                && (item.DelayedUntilUtc == null || item.DelayedUntilUtc <= nowUtc))
            .OrderBy(item => item.DelayedUntilUtc ?? item.DueAtUtc)
            .ThenBy(item => item.CreatedAtUtc)
            .ToListAsync(ct);

        return tasks
            .Select(task => RecurringTaskPresentationMapper.MapCustomTaskCandidate(task, timezone))
            .ToList();
    }

    private async Task<List<RecurringTaskCandidate>> ExcludeClientVacationCandidatesAsync(
        List<RecurringTaskCandidate> candidates,
        CancellationToken ct)
    {
        var clientCandidates = candidates.Where(candidate => candidate.ClientId is not null).ToList();
        if (clientCandidates.Count == 0)
        {
            return candidates;
        }

        var clientIds = clientCandidates.Select(candidate => candidate.ClientId!.Value).Distinct().ToList();
        var vacations = await db.ClientVacations
            .AsNoTracking()
            .Where(vacation => clientIds.Contains(vacation.ClientId))
            .Select(vacation => new { vacation.ClientId, vacation.StartDate, vacation.EndDate })
            .ToListAsync(ct);

        return candidates
            .Where(candidate => candidate.ClientId is null || !vacations.Any(vacation =>
                vacation.ClientId == candidate.ClientId
                && vacation.StartDate <= candidate.BusinessDate
                && vacation.EndDate >= candidate.BusinessDate))
            .ToList();
    }


    private static bool HasAnyClientContact(Client client)
    {
        return !string.IsNullOrWhiteSpace(client.Contacts.Phone)
               || !string.IsNullOrWhiteSpace(client.Contacts.Telegram)
               || !string.IsNullOrWhiteSpace(client.Contacts.Vk);
    }

    private static string BuildAppointmentReminderDeduplicationKey(Ulid ruleId, Ulid appointmentId, DateTime appointmentStartUtc, int offsetMinutes)
    {
        var normalizedStartUtc = appointmentStartUtc.Kind == DateTimeKind.Utc
            ? appointmentStartUtc
            : DateTime.SpecifyKind(appointmentStartUtc, DateTimeKind.Utc);

        return $"appointment-reminder:{ruleId}:{appointmentId}:{normalizedStartUtc:yyyyMMddHHmmss}:{offsetMinutes}";
    }

    private sealed class DebtorAppointmentLedger
    {
        public required Ulid AppointmentId { get; init; }
        public required DateTime StartDate { get; init; }
        public required decimal Price { get; init; }
        public decimal RemainingAmount { get; set; }
    }

    private sealed record DebtorServicePrice(DateTime EffectiveDateUtc, decimal Price);

    private sealed record DebtorAppointment(
        Ulid Id,
        Client Client,
        Ulid ServiceId,
        DateTime StartDate);

    private sealed record DebtorPayment(Ulid ClientId, decimal Amount, DateTime Date);

    private sealed record DebtorReminderData(
        DateOnly TodayLocal,
        List<DebtorAppointment> Appointments,
        IReadOnlyDictionary<Ulid, List<DebtorPayment>> PaymentsByClient,
        IReadOnlyDictionary<Ulid, List<DebtorServicePrice>> PriceLookup,
        IReadOnlyCollection<int> StageStartDays);

    private static decimal ResolveDebtorAppointmentPrice(
        Ulid serviceId,
        DateTime appointmentStartUtc,
        IReadOnlyDictionary<Ulid, List<DebtorServicePrice>> priceLookup)
    {
        if (!priceLookup.TryGetValue(serviceId, out var prices))
        {
            return 0m;
        }

        return prices
            .Where(price => price.EffectiveDateUtc <= appointmentStartUtc)
            .OrderByDescending(price => price.EffectiveDateUtc)
            .Select(price => price.Price)
            .FirstOrDefault();
    }

    private static int GetDebtorReminderStageStartDays(int initialDelayDays, int? repeatEveryDays)
    {
        return initialDelayDays + (repeatEveryDays is > 0 ? repeatEveryDays.Value : 0);
    }

    private static int[] GetDebtorReminderStageStartDays(IEnumerable<RecurringTaskRule> rules)
    {
        return rules
            .Select(rule => GetDebtorReminderStageStartDays(
                Math.Max(1, (rule.OffsetMinutes ?? 24 * 60) / (24 * 60)),
                rule.CooldownDays))
            .ToArray();
    }

}
