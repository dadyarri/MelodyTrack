using MelodyTrack.Backend.ErrorHandling;
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

namespace MelodyTrack.Backend.Api.Services.Endpoints;

[ApiEndpoint(ApiMethod.Delete, "/services/{id}")]
public sealed class DeleteServiceEndpoint
{

    [Microsoft.AspNetCore.Authorization.Authorize(Policy = MelodyTrack.Backend.Api.Auth.AuthorizationPolicies.Administrator)]
    public static async Task<Results<NoContent, NotFound<ApiProblemDetails>, ApiProblemDetails, UnauthorizedHttpResult, ForbidHttpResult, Conflict<StaleEntityConflictResponse>>> HandleAsync(
        [AsParameters] GetEntityRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        IAuditLogService auditLogService,
        IEntityFreshnessService entityFreshnessService,
        ApiValidationErrorCollection validationErrors,
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

        var service = await db.Services
            .AsNoTracking()
            .Where(item => item.Id == req.Id)
            .Select(item => new { item.Id, item.Name, item.Description })
            .FirstOrDefaultAsync(ct);

        if (service is null)
        {
            return TypedResults.NoContent();
        }

        var conflict = await entityFreshnessService.GetConflictIfStaleAsync(
            "service",
            service.Id,
            req.ExpectedActivityId,
            "Услуга была изменена другим пользователем. Проверьте последние изменения перед удалением.",
            ct);

        if (conflict is not null)
        {
            return TypedResults.Conflict(conflict);
        }

        var hasPayments = await db.Payments.AnyAsync(item => item.Service != null && item.Service.Id == req.Id, ct);
        var hasAppointments = await db.Appointments.AnyAsync(item => item.Service.Id == req.Id, ct);
        var hasRecurringRules = await db.RecurrenceRules.AnyAsync(item => item.Service.Id == req.Id, ct);

        if (hasPayments || hasAppointments || hasRecurringRules)
        {
            validationErrors.Add(nameof(req.Id), "Нельзя удалить услугу, которая уже используется в платежах или расписании.");
            return new ApiProblemDetails(validationErrors);
        }

        await db.Services.Where(item => item.Id == req.Id).ExecuteDeleteAsync(ct);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Event = MelodyTrack.Core.Auditing.AuditCatalog.Events.ServiceDeleted,
            EntityType = "service",
            EntityId = service.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Услуга", service.Name),
                AuditDetailsFormatter.DescribeContext("Описание", service.Description)
            )
        }, ct);

        return TypedResults.NoContent();
    }
}
