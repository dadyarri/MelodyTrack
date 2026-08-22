using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Schedule;
using MelodyTrack.Backend.Api.Schedule.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Schedule.Endpoints;

[ApiEndpoint(ApiMethod.Patch, "/appointments/{id}")]
public sealed class UpdateAppointmentEndpoint
{
    private static readonly IReadOnlyDictionary<AppointmentUpdatePreparationError, (string Field, string Message, int Status)> PreparationErrors =
        new Dictionary<AppointmentUpdatePreparationError, (string, string, int)>
        {
            [AppointmentUpdatePreparationError.InvalidStatus] = (nameof(UpdateAppointmentRequest.Status), "Некорректный статус записи", StatusCodes.Status400BadRequest),
            [AppointmentUpdatePreparationError.ClientNotFound] = (nameof(UpdateAppointmentRequest.ClientId), "Клиент не найден", StatusCodes.Status404NotFound),
            [AppointmentUpdatePreparationError.ServiceNotFound] = (nameof(UpdateAppointmentRequest.ServiceId), "Услуга не найдена", StatusCodes.Status404NotFound),
            [AppointmentUpdatePreparationError.ProviderNotFound] = (nameof(UpdateAppointmentRequest.ProviderId), "Пользователь не найден", StatusCodes.Status404NotFound),
            [AppointmentUpdatePreparationError.CourseThemeNotFound] = (nameof(UpdateAppointmentRequest.CourseThemeId), "Тема курса не найдена", StatusCodes.Status404NotFound),
            [AppointmentUpdatePreparationError.CourseThemeUnavailable] = (nameof(UpdateAppointmentRequest.CourseThemeId), "Эта тема недоступна для выбранного клиента.", StatusCodes.Status400BadRequest),
            [AppointmentUpdatePreparationError.MissingTimezone] = (nameof(UpdateAppointmentRequest.Timezone), "Нужно указать таймзону.", StatusCodes.Status400BadRequest),
            [AppointmentUpdatePreparationError.ProviderUnavailable] = (nameof(UpdateAppointmentRequest.StartDate), "Запись попадает в нерабочее время преподавателя или в отпуск.", StatusCodes.Status400BadRequest),
            [AppointmentUpdatePreparationError.MissingRecurrencePattern] = (nameof(UpdateAppointmentRequest.RecurrencePattern), "Паттерн повторения не указан", StatusCodes.Status404NotFound),
            [AppointmentUpdatePreparationError.MissingRecurrenceStartDate] = (nameof(UpdateAppointmentRequest.StartDate), "Дата начала не задана", StatusCodes.Status404NotFound),
            [AppointmentUpdatePreparationError.RecurrenceTypeNotFound] = (nameof(UpdateAppointmentRequest.RecurrenceTypeId), "Тип повторения не найден", StatusCodes.Status404NotFound)
        };

    public static async Task<Results<NoContent, UnauthorizedHttpResult, NotFound<ApiProblemDetails>, ApiProblemDetails, Conflict<StaleEntityConflictResponse>>> HandleAsync(
        UpdateAppointmentRequest req,
        Ulid id,
        AppDbContext db,
        IAuditLogService auditLogService,
        IEntityFreshnessService entityFreshnessService,
        AppointmentUpdatePreparationService preparationService,
        HttpContext httpContext,
        ApiValidationErrorCollection validationErrors,
        CancellationToken ct
    )
    {
        req.Id = id;
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
            validationErrors.Add(nameof(req.Id), "Встреча не найдена");
            return TypedResults.NotFound(new ApiProblemDetails(validationErrors, httpContext, StatusCodes.Status404NotFound));
        }

        var conflict = await entityFreshnessService.GetConflictIfStaleAsync(
            "appointment",
            appointment.Id,
            req.ExpectedActivityId,
            "Запись была изменена другим пользователем. Обновите данные или повторите сохранение поверх новой версии.",
            ct);

        if (conflict is not null && !AppointmentUpdateComparer.IsNoOp(appointment, req))
        {
            return TypedResults.Conflict(conflict);
        }

        var preparation = await preparationService.PrepareAsync(appointment, req, ct);
        if (preparation.Error != AppointmentUpdatePreparationError.None)
        {
            return CreatePreparationError(preparation.Error, validationErrors, httpContext);
        }

        if (appointment.RecurringRule is not null && preparation.Changes.StartDateChanged && preparation.Scope != AppointmentUpdateScope.Single)
        {
            await RescheduleRecurringSeriesAsync(db, appointment, req.StartDate!.Value, preparation.Scope, ct);
            await auditLogService.WriteAsync(new AuditLogWriteRequest
            {
                Category = "schedule",
                Action = preparation.Scope == AppointmentUpdateScope.All ? "recurring_appointments_rescheduled" : "recurring_appointments_split_and_rescheduled",
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

        if (appointment.RecurringRule is not null && preparation.Changes.RequiresRecurringDetachment)
        {
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
                EndDate = (req.StartDate ?? appointment.StartDate).Add(preparation.Duration),
                Status = preparation.RequestedStatus ?? appointment.Status,
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

        if (preparation.RequestedStatus is not null)
        {
            appointment.Status = preparation.RequestedStatus.Value;
        }

        var recurrenceError = await preparationService.ApplyRecurrenceAsync(appointment, req, ct);
        if (recurrenceError != AppointmentUpdatePreparationError.None)
        {
            return CreatePreparationError(recurrenceError, validationErrors, httpContext);
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

    private static ApiProblemDetails CreatePreparationError(
        AppointmentUpdatePreparationError error,
        ApiValidationErrorCollection validationErrors,
        HttpContext httpContext)
    {
        var (field, message, status) = PreparationErrors[error];
        validationErrors.Add(field, message);
        return new ApiProblemDetails(validationErrors, httpContext, status);
    }

    private static async Task RescheduleRecurringSeriesAsync(
        AppDbContext db,
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

}
