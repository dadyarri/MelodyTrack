using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Payments.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Payments.Endpoints;

public class UpdatePaymentEndpoint(AppDbContext db, IAuditLogService auditLogService, IEntityFreshnessService entityFreshnessService)
    : Ep.Req<UpdatePaymentRequest>.Res<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>, Conflict<StaleEntityConflictResponse>>>
{
    public override void Configure()
    {
        Put("/payments/{id}");
    }

    public override async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>, Conflict<StaleEntityConflictResponse>>> ExecuteAsync(
        UpdatePaymentRequest req,
        CancellationToken ct)
    {
        var currentUserRole = await EndpointAuthUtils.GetCurrentUserRoleAsync(User, db, ct);
        if (currentUserRole is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!currentUserRole.Value.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        var payment = await db.Payments
            .Include(e => e.Client)
            .Include(e => e.Service)
            .FirstOrDefaultAsync(e => e.Id == req.Id, ct);

        if (payment is null)
        {
            AddError(e => e.Id, "Платеж не найден");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        var client = await db.Clients.FirstOrDefaultAsync(e => e.Id == req.ClientId, ct);
        if (client is null)
        {
            AddError(e => e.ClientId, "Клиент не найден");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        Service? service = null;
        if (req.ServiceId.HasValue)
        {
            service = await db.Services.FirstOrDefaultAsync(e => e.Id == req.ServiceId.Value, ct);
            if (service is null)
            {
                AddError(e => e.ServiceId, "Сервис не найден");
                return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
            }
        }

        var conflict = await entityFreshnessService.GetConflictIfStaleAsync(
            "payment",
            payment.Id,
            req.ExpectedActivityId,
            "Платеж был изменен другим пользователем. Обновите данные или повторите сохранение поверх новой версии.",
            ct);

        if (conflict is not null && !IsNoOp(payment, client.Id, service?.Id, req))
        {
            return TypedResults.Conflict(conflict);
        }

        var beforeClientName = $"{payment.Client.LastName} {payment.Client.FirstName}".Trim();
        var beforeServiceName = payment.Service?.Name;
        var beforeAmount = payment.Amount;
        var beforeDate = payment.Date;
        var beforeDescription = payment.Description;

        payment.Client = client;
        payment.Service = service;
        payment.Amount = req.Amount;
        payment.Date = req.Date;
        payment.Description = req.Description ?? string.Empty;

        await db.SaveChangesAsync(ct);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "payments",
            Action = "payment_updated",
            EntityType = "payment",
            EntityId = payment.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeChange("Клиент", beforeClientName, $"{client.LastName} {client.FirstName}".Trim()),
                AuditDetailsFormatter.DescribeChange("Услуга", beforeServiceName, service?.Name),
                AuditDetailsFormatter.DescribeChange("Сумма", beforeAmount.ToString("0.##"), payment.Amount.ToString("0.##")),
                AuditDetailsFormatter.DescribeChange("Дата", beforeDate, payment.Date),
                AuditDetailsFormatter.DescribeChange("Описание", beforeDescription, payment.Description)
            )
        }, ct);

        return TypedResults.NoContent();
    }

    private static bool IsNoOp(Payment payment, Ulid clientId, Ulid? serviceId, UpdatePaymentRequest req)
    {
        return payment.Client.Id == clientId
               && payment.Service?.Id == serviceId
               && payment.Amount == req.Amount
               && payment.Date == req.Date
               && payment.Description == (req.Description ?? string.Empty);
    }
}
