using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Payments.Endpoints;

public class DeletePaymentEndpoint(AppDbContext db, IAuditLogService auditLogService) : Ep.Req<GetEntityRequest>.Res<Results<NoContent, UnauthorizedHttpResult, NotFound<ProblemDetails>, Conflict<StaleEntityConflictResponse>>>
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

        var latestActivity = await db.AuditLogs
            .AsNoTracking()
            .Where(item => item.EntityType == "payment" && item.EntityId == payment.Id.ToString())
            .OrderByDescending(item => item.CreatedAtUtc)
            .Select(item => new RecordActivityDto
            {
                Id = item.Id,
                CreatedAtUtc = item.CreatedAtUtc,
                Category = item.Category,
                Action = item.Action,
                ActorEmail = item.ActorEmail,
                ActorDisplayName = item.ActorDisplayName,
                SourceIpAddress = item.SourceIpAddress,
                Details = item.Details
            })
            .FirstOrDefaultAsync(ct);

        if (EntityFreshnessUtils.IsStale(req.ExpectedActivityId, latestActivity))
        {
            return TypedResults.Conflict(EntityFreshnessUtils.CreateConflict(
                "payment",
                payment.Id,
                "Платеж был изменен другим пользователем. Проверьте последние изменения перед удалением.",
                latestActivity));
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
