using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Services.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Services.Endpoints;

[ApiEndpoint(ApiMethod.Patch, "/services/{id}/price")]
public sealed class UpdateServicePriceEndpoint
{

    [Microsoft.AspNetCore.Authorization.Authorize(Policy = MelodyTrack.Backend.Api.Auth.AuthorizationPolicies.Administrator)]
    public static async Task<Results<Ok<CreateEntityResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound, Conflict<StaleEntityConflictResponse>>> HandleAsync(
        UpdateServicePriceRequest req,
        Ulid id,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        IAuditLogService auditLogService,
        IEntityFreshnessService entityFreshnessService,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        req.Id = id;
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
            .Where(e => e.Id == req.Id)
            .FirstOrDefaultAsync(ct);

        if (service is null)
        {
            return TypedResults.NotFound();
        }

        var conflict = await entityFreshnessService.GetConflictIfStaleAsync(
            "service",
            service.Id,
            req.ExpectedActivityId,
            "Цена услуги была изменена другим пользователем. Обновите данные и повторите изменение.",
            ct);

        if (conflict is not null)
        {
            return TypedResults.Conflict(conflict);
        }

        var previousPrice = await db.ServicePriceHistory
            .Where(item => item.Service.Id == service.Id)
            .OrderByDescending(item => item.EffectiveDate)
            .Select(item => (decimal?)item.Price)
            .FirstOrDefaultAsync(ct);

        var price = new ServicePrice
        {
            Id = Ulid.NewUlid(),
            EffectiveDate = timeProvider.GetUtcNow().UtcDateTime,
            Price = req.Price,
            Service = service
        };

        await db.ServicePriceHistory.AddAsync(price, ct);
        await db.SaveChangesAsync(ct);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Event = MelodyTrack.Core.Auditing.AuditCatalog.Events.ServicePriceUpdated,
            EntityType = "service",
            EntityId = service.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Услуга", service.Name),
                AuditDetailsFormatter.DescribeChange("Цена", previousPrice?.ToString("0.##"), req.Price.ToString("0.##"))
            )
        }, ct);

        return TypedResults.Ok(new CreateEntityResponse
        {
            Id = service.Id
        });
    }
}
