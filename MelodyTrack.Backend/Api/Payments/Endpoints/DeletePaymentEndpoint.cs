using FastEndpoints;
using MelodyTrack.Common.Api.Common.Requests;
using MelodyTrack.Common.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Payments.Endpoints;

public class DeletePaymentEndpoint(AppDbContext db) : Ep.Req<GetEntityRequest>.Res<Results<NoContent, UnauthorizedHttpResult, NotFound<ProblemDetails>>>
{
    public override void Configure()
    {
        Delete("/payments/{id}");
    }

    public override async Task<Results<NoContent, UnauthorizedHttpResult, NotFound<ProblemDetails>>> ExecuteAsync(GetEntityRequest req, CancellationToken ct)
    {
        Logger.LogDebug("Attempting to delete payment with ID: {PaymentId}", req.Id);
        var rowsDeleted = await db.Payments.Where(e => e.Id == req.Id).ExecuteDeleteAsync(ct);

        if (rowsDeleted == 0)
        {
            Logger.LogWarning("Failed to delete payment: ID {PaymentId} not found", req.Id);
            AddError(r => r.Id, "Платёж не найден");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        Logger.LogInformation("Successfully deleted payment with ID: {PaymentId}", req.Id);
        return TypedResults.NoContent();
    }
}