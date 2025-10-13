using Backend.Api.Base.Models;
using Backend.Api.Payments.Models;
using Backend.Data;
using Backend.Data.Entities;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Payments.Endpoints;

/// <summary>
///     Создать платёж
/// </summary>
/// <param name="db">БД</param>
public class CreatePaymentEndpoint(AppDbContext db)
    : Endpoint<CreatePaymentRequest, Results<Ok<CreateEntityResponse>, NotFound, ProblemDetails>>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Post("/payments");
    }

    /// <inheritdoc />
    public override async Task<Results<Ok<CreateEntityResponse>, NotFound, ProblemDetails>> ExecuteAsync(
        CreatePaymentRequest req,
        CancellationToken ct)
    {
        var client = await db.Clients
            .Where(e => e.Id == req.ClientId)
            .FirstOrDefaultAsync(ct);

        if (client == null) return TypedResults.NotFound();

        var payment = new Payment
        {
            Client = client,
            Description = req.Description,
            Amount = req.Amount,
            Date = DateTime.UtcNow
        };

        await db.Payments.AddAsync(payment, ct);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new CreateEntityResponse
        {
            Id = payment.Id
        });
    }
}