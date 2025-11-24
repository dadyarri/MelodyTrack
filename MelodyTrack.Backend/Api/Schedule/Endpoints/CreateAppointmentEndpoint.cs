using FastEndpoints;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Api.Schedule.Requests;
using MelodyTrack.Common.Data;
using MelodyTrack.Common.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Schedule.Endpoints;

public class CreateAppointmentEndpoint(AppDbContext db) : Ep.Req<CreateAppointmentRequest>.Res<IResult>
{
    public override void Configure()
    {
        Post("/appointments");
    }

    public override async Task<IResult> ExecuteAsync(CreateAppointmentRequest req, CancellationToken ct)
    {
        var client = await db.Clients.Where(e => e.Id == req.ClientId).FirstOrDefaultAsync(ct);

        if (client is null)
        {
            AddError(e => e.ClientId, "Клиент не найден");
            return ApiResults.NotFound(ValidationFailures);
        }

        var service = await db.Services.Where(e => e.Id == req.ServiceId).FirstOrDefaultAsync(ct);

        if (service is null)
        {
            AddError(e => e.ServiceId, "Сервис не найден");
            return ApiResults.NotFound(ValidationFailures);
        }

        var provider = await db.Users.Where(e => e.Id == req.ProviderId).FirstOrDefaultAsync(ct);

        var recurrenceType = await db.RecurrenceTypes.Where(e => e.Id == req.RecurrenceTypeId).FirstOrDefaultAsync(ct);

        AppointmentRecurrenceRule? recurrenceRule = null;

        if (recurrenceType is not null)
        {
            recurrenceRule = new AppointmentRecurrenceRule
            {
                Service = service,
                Client = client,
                Provider = provider,
                StartDate = req.StartDate,
                EndDate = req.PatternEndDate,
                RecurrenceType = recurrenceType,
                RecurrencePattern = req.RecurrencePattern
            };
        }

        var appointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            Provider = provider,
            StartDate = req.StartDate.ToUniversalTime(),
            EndDate = req.StartDate.AddHours(1).ToUniversalTime(),
            IsCanceled = false,
            IsCompleted = false,
            RecurringRule = recurrenceRule
        };

        await db.AddAsync(appointment, ct);
        await db.SaveChangesAsync(ct);

        return ApiResults.Created($"/appointments/{appointment.Id}", new CreateEntityResponse { Id = appointment.Id });
    }
}