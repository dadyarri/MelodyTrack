using Backend.Data;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Payments.Endpoints;

/// <summary>
///     Удалить платёж
/// </summary>
/// <param name="db">БД</param>
public class DeletePaymentEndpoint(AppDbContext db)
    : Endpoint<EmptyRequest, Results<NoContent, NotFound, ProblemDetails>>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Delete("/payments/{id:long}");
    }

    /// <inheritdoc />
    public override async Task<Results<NoContent, NotFound, ProblemDetails>> ExecuteAsync(
        EmptyRequest req, CancellationToken ct)
    {
        var paymentId = Route<long>("id");

        var payment = await db.Payments.Where(e => e.Id == paymentId).FirstOrDefaultAsync(ct);

        if (payment == null) return TypedResults.NotFound();

        db.Remove(payment);

        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }
}