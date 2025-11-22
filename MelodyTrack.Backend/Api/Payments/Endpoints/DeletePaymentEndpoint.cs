using FastEndpoints;
using MelodyTrack.Common.Api.Common.Requests;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Payments.Endpoints;

public class DeletePaymentEndpoint(AppDbContext db) : Ep.Req<GetEntityRequest>.Res<IResult>
{
    public override void Configure()
    {
        Delete("/payments/{id}");
    }

    public override async Task<IResult> ExecuteAsync(GetEntityRequest req, CancellationToken ct)
    {
        Logger.LogDebug("Attempting to delete payment with ID: {PaymentId}", req.Id);
        var rowsDeleted = await db.Payments.Where(e => e.Id == req.Id).ExecuteDeleteAsync(ct);

        if (rowsDeleted == 0)
        {
            Logger.LogWarning("Failed to delete payment: ID {PaymentId} not found", req.Id);
            AddError(r => r.Id, "Платёж не найден");
            return ApiResults.NotFound(ValidationFailures);
        }

        Logger.LogInformation("Successfully deleted payment with ID: {PaymentId}", req.Id);
        return ApiResults.NoContent();
    }
}