using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Payments.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Payments.Endpoints;

public class CreatePaymentEndpoint(AppDbContext db) : Ep.Req<CreatePaymentRequest>.Res<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, NotFound<ProblemDetails>>>
{
    public override void Configure()
    {
        Post("/payments");
    }

    public override async Task<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, NotFound<ProblemDetails>>> ExecuteAsync(CreatePaymentRequest req, CancellationToken ct)
    {

        var service = await db.Services.Where(e => e.Id == req.ServiceId)
            .FirstOrDefaultAsync(ct);

        if (service is null)
        {
            AddError(r => r.ServiceId, "Сервис не найден");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        var client = await db.Clients.Where(e => e.Id == req.ClientId)
            .FirstOrDefaultAsync(ct);

        if (client is null)
        {
            AddError(r => r.ClientId, "Клиент не найден");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        var payment = new Payment
        {
            Id = Ulid.NewUlid(),
            Amount = req.Amount,
            Client = client,
            Date = req.Date,
            Description = req.Description,
            Service = service
        };

        await db.Payments.AddAsync(payment, ct);
        await db.SaveChangesAsync(ct);

        Logger.LogInformation("Created new payment: {Description} with amount {Amount}", payment.Description, payment.Amount);

        return TypedResults.Created($"/payments/{payment.Id}", new CreateEntityResponse
        {
            Id = payment.Id
        });
    }
}