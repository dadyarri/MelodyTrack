using FastEndpoints;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Api.Schedule.Requests;
using MelodyTrack.Common.Data;
using MelodyTrack.Common.Data.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Schedule.Endpoints;

public class UpdateAppointmentEndpoint(AppDbContext db) : Ep.Req<UpdateAppointmentRequest>.Res<IResult>
{
    public override void Configure()
    {
        Patch("/appointments/{id}");
    }

    public override async Task<IResult> ExecuteAsync(UpdateAppointmentRequest req, CancellationToken ct)
    {
        var appointment = await db.Appointments
            .Where(e => e.Id == req.Id)
            .Include(e => e.Service)
            .Include(e => e.Client)
            .Include(e => e.RecurringRule)
            .FirstOrDefaultAsync(ct);

        if (appointment is null)
        {
            AddError(r => r.Id, "Встреча не найдена");
            return ApiResults.NotFound(ValidationFailures);
        }

        if (req.ClientId is not null)
        {
            var client = await db.Clients.FirstOrDefaultAsync(e => e.Id == req.ClientId.Value, ct);
            if (client is null)
            {
                AddError(r => r.ClientId, "Клиент не найден");
                return ApiResults.NotFound(ValidationFailures);
            }

            appointment.Client = client;
        }

        if (req.ServiceId is not null)
        {
            var service = await db.Services.FirstOrDefaultAsync(e => e.Id == req.ServiceId.Value, ct);
            if (service is null)
            {
                AddError(r => r.ServiceId, "Услуга не найдена");
                return ApiResults.NotFound(ValidationFailures);
            }

            appointment.Service = service;
        }

        if (req.ProviderId is not null)
        {
            var provider = await db.Users.FirstOrDefaultAsync(e => e.Id == req.ProviderId.Value, ct);
            if (provider is null)
            {
                AddError(r => r.ProviderId, "Пользователь не найден");
                return ApiResults.NotFound(ValidationFailures);
            }

            appointment.Provider = provider;
        }

        if (req.StartDate is not null)
        {
            appointment.StartDate = req.StartDate.Value;
            appointment.EndDate = req.StartDate.Value.AddHours(1);
        }

        if (req.IsCompleted is not null)
        {
            appointment.IsCompleted = req.IsCompleted.Value;
        }

        if (req.IsCanceled is not null)
        {
            appointment.IsCanceled = req.IsCanceled.Value;
        }

        if (req.RecurrenceTypeId is not null)
        {
            if (req.RecurrencePattern is null)
            {
                AddError(r => r.RecurrencePattern, "Паттерн повторения не указан");
                return ApiResults.NotFound(ValidationFailures);
            }

            if (req.StartDate is null)
            {
                AddError(r => r.StartDate, "Дата начала не задана");
                return ApiResults.NotFound(ValidationFailures);
            }

            var recurrenceType = await db.RecurrenceTypes.FirstOrDefaultAsync(e => e.Id == req.RecurrenceTypeId.Value, ct);
            if (recurrenceType is null)
            {
                AddError(r => r.ProviderId, "Тип повторения не найден");
                return ApiResults.NotFound(ValidationFailures);
            }

            appointment.RecurringRule = new AppointmentRecurrenceRule
            {
                Id = Ulid.NewUlid(),
                RecurrenceType = recurrenceType,
                RecurrencePattern = req.RecurrencePattern,
                Client = appointment.Client,
                Service = appointment.Service,
                StartDate = appointment.RecurringRule?.StartDate ?? req.StartDate.Value
            };
        }

        await db.SaveChangesAsync(ct);

        return ApiResults.NoContent();
    }
}