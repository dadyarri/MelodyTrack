using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Schedule.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Schedule.Endpoints;

public class UpdateAppointmentEndpoint(AppDbContext db, IAuditLogService auditLogService, IEntityFreshnessService entityFreshnessService, IUserAvailabilityService userAvailabilityService) : Ep.Req<UpdateAppointmentRequest>.Res<Results<NoContent, UnauthorizedHttpResult, NotFound<ProblemDetails>, ProblemDetails, Conflict<StaleEntityConflictResponse>>>
{
    public override void Configure()
    {
        Patch("/appointments/{id}");
    }

    public override async Task<Results<NoContent, UnauthorizedHttpResult, NotFound<ProblemDetails>, ProblemDetails, Conflict<StaleEntityConflictResponse>>> ExecuteAsync(UpdateAppointmentRequest req, CancellationToken ct)
    {
        var appointment = await db.Appointments
            .Where(e => e.Id == req.Id && !e.IsDeleted)
            .Include(e => e.Service)
            .Include(e => e.Client)
            .Include(e => e.Provider)
            .Include(e => e.CourseTheme)
                .ThenInclude(item => item!.Branch)
                    .ThenInclude(item => item!.Block)
                        .ThenInclude(item => item!.Course)
            .Include(e => e.RecurringRule)
            .ThenInclude(rule => rule!.RecurrenceType)
            .FirstOrDefaultAsync(ct);

        var beforeStartDateUtc = appointment?.StartDate;
        var beforeStatus = appointment?.Status.ToDisplayName();
        var beforeCourseTheme = appointment?.CourseTheme?.Title;
        var beforeLessonNotes = appointment?.LessonNotes;

        if (appointment is null)
        {
            AddError(r => r.Id, "Встреча не найдена");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        var conflict = await entityFreshnessService.GetConflictIfStaleAsync(
            "appointment",
            appointment.Id,
            req.ExpectedActivityId,
            "Запись была изменена другим пользователем. Обновите данные или повторите сохранение поверх новой версии.",
            ct);

        if (conflict is not null && !IsNoOp(appointment, req))
        {
            return TypedResults.Conflict(conflict);
        }

        AppointmentStatus? requestedStatus = null;
        if (req.Status is not null)
        {
            if (!AppointmentStatusExtensions.TryParseApiKey(req.Status, out var parsedStatus))
            {
                AddError(r => r.Status, "Некорректный статус записи");
                return new ProblemDetails(ValidationFailures);
            }

            requestedStatus = parsedStatus;
        }

        var clientChanged = req.ClientId is not null && req.ClientId.Value != appointment.Client.Id;
        var serviceChanged = req.ServiceId is not null && req.ServiceId.Value != appointment.Service.Id;
        var providerChanged = req.ProviderId is not null && req.ProviderId.Value != appointment.Provider?.Id;
        var startDateChanged = req.StartDate is not null && req.StartDate.Value != appointment.StartDate;
        var courseThemeChanged = req.HasCourseThemeSelection && req.CourseThemeId != appointment.CourseThemeId;
        var lessonNotesChanged = req.HasLessonNotes && NormalizeLessonNotes(req.LessonNotes) != appointment.LessonNotes;
        var nextStartDate = req.StartDate ?? appointment.StartDate;
        var nextDuration = appointment.EndDate - appointment.StartDate;
        var requestedScope = ParseScope(req.Scope);

        if (req.ClientId is not null)
        {
            var client = await db.Clients.FirstOrDefaultAsync(e => e.Id == req.ClientId.Value, ct);
            if (client is null)
            {
                AddError(r => r.ClientId, "Клиент не найден");
                return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
            }

            appointment.Client = client;
        }

        if (req.ServiceId is not null)
        {
            var service = await db.Services.FirstOrDefaultAsync(e => e.Id == req.ServiceId.Value, ct);
            if (service is null)
            {
                AddError(r => r.ServiceId, "Услуга не найдена");
                return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
            }

            appointment.Service = service;
        }

        if (req.ProviderId is not null)
        {
            var provider = await db.Users.FirstOrDefaultAsync(e => e.Id == req.ProviderId.Value, ct);
            if (provider is null)
            {
                AddError(r => r.ProviderId, "Пользователь не найден");
                return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
            }

            appointment.Provider = provider;
        }

        if (req.HasCourseThemeSelection)
        {
            if (req.CourseThemeId is null)
            {
                appointment.CourseTheme = null;
                appointment.CourseThemeId = null;
            }
            else
            {
                var courseTheme = await db.CourseThemes
                    .Include(item => item.Branch)
                        .ThenInclude(item => item.Block)
                            .ThenInclude(item => item.Course)
                    .FirstOrDefaultAsync(item => item.Id == req.CourseThemeId.Value, ct);

                if (courseTheme is null)
                {
                    AddError(r => r.CourseThemeId, "Тема курса не найдена");
                    return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
                }

                var hasEnrollment = await db.CourseEnrollments
                    .AsNoTracking()
                    .AnyAsync(item => item.ClientId == appointment.Client.Id && item.CourseId == courseTheme.Branch.Block.CourseId, ct);

                if (!hasEnrollment)
                {
                    AddError(r => r.CourseThemeId, "Эта тема недоступна для выбранного клиента.");
                    return new ProblemDetails(ValidationFailures);
                }

                appointment.CourseTheme = courseTheme;
                appointment.CourseThemeId = courseTheme.Id;
            }
        }

        if (req.HasLessonNotes)
        {
            appointment.LessonNotes = NormalizeLessonNotes(req.LessonNotes);
        }

        if (appointment.Provider is not null && (providerChanged || startDateChanged))
        {
            if (string.IsNullOrWhiteSpace(req.Timezone))
            {
                AddError(r => r.Timezone, "Нужно указать таймзону.");
                return new ProblemDetails(ValidationFailures);
            }

            var isAvailable = await userAvailabilityService.IsAvailableAsync(
                appointment.Provider.Id,
                nextStartDate.ToUniversalTime(),
                nextStartDate.Add(nextDuration).ToUniversalTime(),
                req.Timezone,
                ct);

            if (!isAvailable)
            {
                AddError(r => r.StartDate, "Запись попадает в нерабочее время преподавателя или в отпуск.");
                return new ProblemDetails(ValidationFailures);
            }
        }

        if (appointment.RecurringRule is not null && startDateChanged && requestedScope != AppointmentUpdateScope.Single)
        {
            await RescheduleRecurringSeriesAsync(appointment, req.StartDate!.Value, requestedScope, ct);
            await auditLogService.WriteAsync(new AuditLogWriteRequest
            {
                Category = "schedule",
                Action = requestedScope == AppointmentUpdateScope.All ? "recurring_appointments_rescheduled" : "recurring_appointments_split_and_rescheduled",
                EntityType = "appointment",
                EntityId = appointment.Id.ToString(),
                Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Клиент", FormatClientDisplayName(appointment.Client)),
                AuditDetailsFormatter.DescribeContext("Услуга", appointment.Service.Name),
                AuditDetailsFormatter.DescribeContext("Преподаватель", FormatProviderDisplayName(appointment.Provider)),
                AuditDetailsFormatter.DescribeContext("Начало", appointment.StartDate),
                AuditDetailsFormatter.DescribeChange("Начало", beforeStartDateUtc, req.StartDate)
            )
            }, ct);

            return TypedResults.NoContent();
        }

        if (appointment.RecurringRule is not null && (clientChanged || serviceChanged || providerChanged || startDateChanged || courseThemeChanged || lessonNotesChanged))
        {
            var duration = appointment.EndDate - appointment.StartDate;
            var updatedAppointment = new Appointment
            {
                Id = Ulid.NewUlid(),
                Client = appointment.Client,
                Service = appointment.Service,
                Provider = appointment.Provider,
                CourseTheme = appointment.CourseTheme,
                CourseThemeId = appointment.CourseThemeId,
                LessonNotes = appointment.LessonNotes,
                StartDate = req.StartDate ?? appointment.StartDate,
                EndDate = (req.StartDate ?? appointment.StartDate).Add(duration),
                Status = requestedStatus ?? appointment.Status,
                IsDeleted = false
            };

            appointment.IsDeleted = true;
            db.Appointments.Add(updatedAppointment);

            await db.SaveChangesAsync(ct);
            await auditLogService.WriteAsync(new AuditLogWriteRequest
            {
                Category = "schedule",
                Action = "recurring_appointment_detached_and_updated",
                EntityType = "appointment",
                EntityId = updatedAppointment.Id.ToString(),
                Details = AuditDetailsFormatter.JoinChanges(
                    AuditDetailsFormatter.DescribeContext("Клиент", FormatClientDisplayName(updatedAppointment.Client)),
                    AuditDetailsFormatter.DescribeContext("Услуга", updatedAppointment.Service.Name),
                    AuditDetailsFormatter.DescribeContext("Преподаватель", FormatProviderDisplayName(updatedAppointment.Provider)),
                    AuditDetailsFormatter.DescribeContext("Тема курса", updatedAppointment.CourseTheme?.Title),
                    AuditDetailsFormatter.DescribeContext("Начало", updatedAppointment.StartDate),
                    AuditDetailsFormatter.DescribeChange("Начало", beforeStartDateUtc, updatedAppointment.StartDate),
                    AuditDetailsFormatter.DescribeChange("Статус", beforeStatus, updatedAppointment.Status.ToDisplayName()),
                    AuditDetailsFormatter.DescribeChange("Тема курса", beforeCourseTheme, updatedAppointment.CourseTheme?.Title),
                    AuditDetailsFormatter.DescribeChange("Заметки урока", beforeLessonNotes, updatedAppointment.LessonNotes)
                )
            }, ct);

            return TypedResults.NoContent();
        }

        if (req.StartDate is not null)
        {
            appointment.StartDate = req.StartDate.Value;
            appointment.EndDate = req.StartDate.Value.AddHours(1);
        }

        if (requestedStatus is not null)
        {
            appointment.Status = requestedStatus.Value;
        }

        if (req.RecurrenceTypeId is not null)
        {
            if (req.RecurrencePattern is null)
            {
                AddError(r => r.RecurrencePattern, "Паттерн повторения не указан");
                return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
            }

            if (req.StartDate is null)
            {
                AddError(r => r.StartDate, "Дата начала не задана");
                return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
            }

            var recurrenceType = await db.RecurrenceTypes.FirstOrDefaultAsync(e => e.Id == req.RecurrenceTypeId.Value, ct);
            if (recurrenceType is null)
            {
                AddError(r => r.ProviderId, "Тип повторения не найден");
                return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
            }

            var recurrenceRule = appointment.RecurringRule ?? new AppointmentRecurrenceRule
            {
                Id = Ulid.NewUlid(),
                RecurrenceType = recurrenceType,
                RecurrencePattern = req.RecurrencePattern,
                Client = appointment.Client,
                Service = appointment.Service,
                Provider = appointment.Provider,
                StartDate = req.StartDate.Value
            };

            recurrenceRule.RecurrenceType = recurrenceType;
            recurrenceRule.RecurrencePattern = req.RecurrencePattern;
            recurrenceRule.Client = appointment.Client;
            recurrenceRule.Service = appointment.Service;
            recurrenceRule.Provider = appointment.Provider;
            recurrenceRule.StartDate = appointment.RecurringRule?.StartDate ?? req.StartDate.Value;

            appointment.RecurringRule = recurrenceRule;
        }

        await db.SaveChangesAsync(ct);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "schedule",
            Action = "appointment_updated",
            EntityType = "appointment",
            EntityId = appointment.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Клиент", FormatClientDisplayName(appointment.Client)),
                AuditDetailsFormatter.DescribeContext("Услуга", appointment.Service.Name),
                AuditDetailsFormatter.DescribeContext("Преподаватель", FormatProviderDisplayName(appointment.Provider)),
                AuditDetailsFormatter.DescribeContext("Тема курса", appointment.CourseTheme?.Title),
                AuditDetailsFormatter.DescribeContext("Начало", appointment.StartDate),
                AuditDetailsFormatter.DescribeChange("Начало", beforeStartDateUtc, appointment.StartDate),
                AuditDetailsFormatter.DescribeChange("Статус", beforeStatus, appointment.Status.ToDisplayName()),
                AuditDetailsFormatter.DescribeChange("Тема курса", beforeCourseTheme, appointment.CourseTheme?.Title),
                AuditDetailsFormatter.DescribeChange("Заметки урока", beforeLessonNotes, appointment.LessonNotes)
            )
        }, ct);

        return TypedResults.NoContent();
    }

    private static bool IsNoOp(Appointment appointment, UpdateAppointmentRequest req)
    {
        var recurrenceTypeChanged = req.RecurrenceTypeId is not null
                                    && req.RecurrenceTypeId != appointment.RecurringRule?.RecurrenceType.Id;
        var recurrencePatternChanged = req.RecurrencePattern is not null
                                       && req.RecurrencePattern != appointment.RecurringRule?.RecurrencePattern;

        var statusChanged = req.Status is not null
                            && (!AppointmentStatusExtensions.TryParseApiKey(req.Status, out var parsedStatus)
                                || parsedStatus != appointment.Status);

        return (req.ClientId is null || req.ClientId == appointment.Client.Id)
               && (req.ServiceId is null || req.ServiceId == appointment.Service.Id)
               && (req.ProviderId is null || req.ProviderId == appointment.Provider?.Id)
               && (!req.HasCourseThemeSelection || req.CourseThemeId == appointment.CourseThemeId)
               && (!req.HasLessonNotes || NormalizeLessonNotes(req.LessonNotes) == appointment.LessonNotes)
               && (req.StartDate is null || req.StartDate == appointment.StartDate)
               && !statusChanged
               && !recurrenceTypeChanged
               && !recurrencePatternChanged;
    }

    private static AppointmentUpdateScope ParseScope(string? rawScope)
    {
        return rawScope?.Trim().ToLowerInvariant() switch
        {
            "this-and-following" => AppointmentUpdateScope.ThisAndFollowing,
            "all" => AppointmentUpdateScope.All,
            _ => AppointmentUpdateScope.Single
        };
    }

    private async Task RescheduleRecurringSeriesAsync(
        Appointment appointment,
        DateTime nextStartDate,
        AppointmentUpdateScope scope,
        CancellationToken ct)
    {
        var recurringRule = appointment.RecurringRule!;
        var delta = nextStartDate - appointment.StartDate;
        var originalRuleEndDate = recurringRule.EndDate;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        if (scope == AppointmentUpdateScope.All || appointment.StartDate.Date <= recurringRule.StartDate.Date)
        {
            recurringRule.StartDate = recurringRule.StartDate.Add(delta);
            recurringRule.EndDate = recurringRule.EndDate?.Add(delta);
            recurringRule.RecurrencePattern = ShiftRecurrencePattern(recurringRule, delta, nextStartDate);

            var recurringAppointments = await db.Appointments
                .Where(item =>
                    item.RecurringRule != null &&
                    item.RecurringRule.Id == recurringRule.Id &&
                    !item.IsDeleted)
                .ToListAsync(ct);

            foreach (var recurringAppointment in recurringAppointments)
            {
                recurringAppointment.StartDate = recurringAppointment.StartDate.Add(delta);
                recurringAppointment.EndDate = recurringAppointment.EndDate.Add(delta);
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return;
        }

        await db.Appointments
            .Where(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == recurringRule.Id &&
                item.StartDate >= appointment.StartDate &&
                !item.IsDeleted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.IsDeleted, true), ct);

        recurringRule.EndDate = appointment.StartDate.Date.AddDays(-1);

        var nextRule = new AppointmentRecurrenceRule
        {
            Id = Ulid.NewUlid(),
            Client = appointment.Client,
            Service = appointment.Service,
            Provider = appointment.Provider,
            StartDate = nextStartDate,
            EndDate = originalRuleEndDate?.Add(delta),
            RecurrenceType = recurringRule.RecurrenceType,
            RecurrencePattern = ShiftRecurrencePattern(recurringRule, delta, nextStartDate)
        };

        await db.RecurrenceRules.AddAsync(nextRule, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static int? ShiftRecurrencePattern(AppointmentRecurrenceRule recurringRule, TimeSpan delta, DateTime nextStartDate)
    {
        return recurringRule.RecurrenceType.Type switch
        {
            AppointmentRecurrenceType.Daily => recurringRule.RecurrencePattern,
            AppointmentRecurrenceType.Monthly => nextStartDate.Day,
            AppointmentRecurrenceType.Weekly => ShiftWeeklyPattern(recurringRule.RecurrencePattern, delta.Days),
            _ => recurringRule.RecurrencePattern
        };
    }

    private static int? ShiftWeeklyPattern(int? currentPattern, int dayOffset)
    {
        if (currentPattern is null or 0)
        {
            return currentPattern;
        }

        var normalizedOffset = ((dayOffset % 7) + 7) % 7;
        if (normalizedOffset == 0)
        {
            return currentPattern;
        }

        var shiftedPattern = 0;
        for (var bitIndex = 0; bitIndex < 7; bitIndex++)
        {
            var currentFlag = 1 << bitIndex;
            if ((currentPattern.Value & currentFlag) == 0)
            {
                continue;
            }

            var shiftedIndex = (bitIndex + normalizedOffset) % 7;
            shiftedPattern |= 1 << shiftedIndex;
        }

        return shiftedPattern;
    }

    private static string FormatClientDisplayName(Client? client)
    {
        if (client is null)
        {
            return "—";
        }

        return $"{client.LastName} {client.FirstName}".Trim();
    }

    private static string FormatProviderDisplayName(User? provider)
    {
        if (provider is null)
        {
            return "—";
        }

        return $"{provider.LastName} {provider.FirstName}".Trim();
    }

    private static string? NormalizeLessonNotes(string? lessonNotes)
    {
        return string.IsNullOrWhiteSpace(lessonNotes) ? null : lessonNotes.Trim();
    }
}

public enum AppointmentUpdateScope
{
    Single,
    ThisAndFollowing,
    All
}
