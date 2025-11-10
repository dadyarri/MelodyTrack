using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Schedule.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Schedule.Endpoints;

public class CreateAppointmentEndpoint(AppDbContext db) : Ep.Req<CreateAppointmentRequest>.Res<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, NotFound<ProblemDetails>>>
{
    public override void Configure()
    {
        Post("/appointments");
    }

    public override async Task<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, NotFound<ProblemDetails>>> ExecuteAsync(CreateAppointmentRequest req, CancellationToken ct)
    {
        var client = await db.Clients.Where(e => e.Id == req.ClientId).FirstOrDefaultAsync(ct);

        if (client is null)
        {
            AddError(e => e.ClientId, "Клиент не найден");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        var service = await db.Services.Where(e => e.Id == req.ServiceId).FirstOrDefaultAsync(ct);

        if (service is null)
        {
            AddError(e => e.ServiceId, "Сервис не найден");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
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

        return TypedResults.Created($"/appointments/{appointment.Id}", new CreateEntityResponse { Id = appointment.Id });
    }
}