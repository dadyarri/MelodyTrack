using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Payments.Endpoints;

public class DeletePaymentEndpoint(AppDbContext db, IAuditLogService auditLogService, IEntityFreshnessService entityFreshnessService) : Ep.Req<GetEntityRequest>.Res<Results<NoContent, UnauthorizedHttpResult, NotFound<ProblemDetails>, Conflict<StaleEntityConflictResponse>>>
{
    public override void Configure()
    {
        Delete("/payments/{id}");
    }

    public override async Task<Results<NoContent, UnauthorizedHttpResult, NotFound<ProblemDetails>, Conflict<StaleEntityConflictResponse>>> ExecuteAsync(GetEntityRequest req, CancellationToken ct)
    {
        Logger.LogDebug("Attempting to delete payment with ID: {PaymentId}", req.Id);
        var payment = await db.Payments
            .AsNoTracking()
            .Where(e => e.Id == req.Id)
            .Select(e => new { e.Id, e.Amount, e.Client.LastName, e.Client.FirstName })
            .FirstOrDefaultAsync(ct);

        if (payment is null)
        {
            Logger.LogInformation("Payment with ID {PaymentId} was already deleted or not found", req.Id);
            return TypedResults.NoContent();
        }

        var conflict = await entityFreshnessService.GetConflictIfStaleAsync(
            "payment",
            payment.Id,
            req.ExpectedActivityId,
            "Платеж был изменен другим пользователем. Проверьте последние изменения перед удалением.",
            ct);

        if (conflict is not null)
        {
            return TypedResults.Conflict(conflict);
        }

        await db.Payments.Where(e => e.Id == req.Id).ExecuteDeleteAsync(ct);

        Logger.LogInformation("Successfully deleted payment with ID: {PaymentId}", req.Id);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "payments",
            Action = "payment_deleted",
            EntityType = "payment",
            EntityId = payment.Id.ToString(),
            Details = $"{payment.LastName} {payment.FirstName}, сумма {payment.Amount}"
        }, ct);
        return TypedResults.NoContent();
    }
}
