using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Payments.Endpoints;

[ApiEndpoint(ApiMethod.Delete, "/payments/{id}")]
public sealed class DeletePaymentEndpoint
{

    [Microsoft.AspNetCore.Authorization.Authorize(Policy = MelodyTrack.Backend.Api.Auth.AuthorizationPolicies.Administrator)]
    public static async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>, Conflict<StaleEntityConflictResponse>>> HandleAsync(
        [AsParameters] GetEntityRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        IAuditLogService auditLogService,
        IEntityFreshnessService entityFreshnessService,
        ILogger<DeletePaymentEndpoint> logger,
        CancellationToken ct
    )
    {
        var currentUserRole = (await currentUserAccessor.GetAsync(ct))?.Role.RoleName;
        if (currentUserRole is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!currentUserRole.Value.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        logger.LogDebug("Attempting to delete payment with ID: {PaymentId}", req.Id);
        var payment = await db.Payments
            .AsNoTracking()
            .Where(e => e.Id == req.Id)
            .Select(e => new { e.Id, e.Amount, e.Date, e.Description, e.Client.LastName, e.Client.FirstName, ServiceName = e.Service != null ? e.Service.Name : null })
            .FirstOrDefaultAsync(ct);

        if (payment is null)
        {
            logger.LogInformation("Payment with ID {PaymentId} was already deleted or not found", req.Id);
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

        logger.LogInformation("Successfully deleted payment with ID: {PaymentId}", req.Id);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Event = MelodyTrack.Core.Auditing.AuditCatalog.Events.PaymentDeleted,
            EntityType = "payment",
            EntityId = payment.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Клиент", $"{payment.LastName} {payment.FirstName}".Trim()),
                AuditDetailsFormatter.DescribeContext("Услуга", payment.ServiceName),
                AuditDetailsFormatter.DescribeContext("Сумма", payment.Amount.ToString("0.##")),
                AuditDetailsFormatter.DescribeContext("Дата", payment.Date),
                AuditDetailsFormatter.DescribeContext("Описание", payment.Description)
            )
        }, ct);
        return TypedResults.NoContent();
    }
}
