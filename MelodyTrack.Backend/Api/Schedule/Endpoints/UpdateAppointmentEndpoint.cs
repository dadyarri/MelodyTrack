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
            .Include(e => e.RecurringRule)
            .FirstOrDefaultAsync(ct);

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
        var nextStartDate = req.StartDate ?? appointment.StartDate;
        var nextDuration = appointment.EndDate - appointment.StartDate;

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

        if (appointment.RecurringRule is not null && (clientChanged || serviceChanged || providerChanged || startDateChanged))
        {
            var duration = appointment.EndDate - appointment.StartDate;
            var updatedAppointment = new Appointment
            {
                Id = Ulid.NewUlid(),
                Client = appointment.Client,
                Service = appointment.Service,
                Provider = appointment.Provider,
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
                Details = $"{updatedAppointment.Client.LastName} {updatedAppointment.Client.FirstName}, {updatedAppointment.Service.Name}, {updatedAppointment.StartDate:O}"
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
            Details = $"{appointment.Client.LastName} {appointment.Client.FirstName}, {appointment.Service.Name}, {appointment.StartDate:O}"
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
               && (req.StartDate is null || req.StartDate == appointment.StartDate)
               && !statusChanged
               && !recurrenceTypeChanged
               && !recurrencePatternChanged;
    }
}
