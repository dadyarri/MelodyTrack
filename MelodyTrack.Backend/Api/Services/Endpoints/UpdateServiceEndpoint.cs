using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Services.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Services.Endpoints;

public class UpdateServiceEndpoint(AppDbContext db, IAuditLogService auditLogService, IEntityFreshnessService entityFreshnessService)
    : Ep.Req<UpdateServiceRequest>.Res<Results<NoContent, NotFound<ProblemDetails>, UnauthorizedHttpResult, Conflict<StaleEntityConflictResponse>>>
{
    public override void Configure()
    {
        Put("/services/{id}");
    }

    public override async Task<Results<NoContent, NotFound<ProblemDetails>, UnauthorizedHttpResult, Conflict<StaleEntityConflictResponse>>> ExecuteAsync(
        UpdateServiceRequest req,
        CancellationToken ct)
    {
        var service = await db.Services.FirstOrDefaultAsync(item => item.Id == req.Id, ct);
        if (service is null)
        {
            AddError(item => item.Id, "Услуга не найдена");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        var conflict = await entityFreshnessService.GetConflictIfStaleAsync(
            "service",
            service.Id,
            req.ExpectedActivityId,
            "Услуга была изменена другим пользователем. Обновите данные и повторите сохранение.",
            ct);

        if (conflict is not null
            && (req.Name != service.Name || req.Description != service.Description))
        {
            return TypedResults.Conflict(conflict);
        }

        var beforeName = service.Name;
        var beforeDescription = service.Description;

        service.Name = req.Name;
        service.Description = req.Description;

        await db.SaveChangesAsync(ct);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "services",
            Action = "service_updated",
            EntityType = "service",
            EntityId = service.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeChange("Название", beforeName, service.Name),
                AuditDetailsFormatter.DescribeChange("Описание", beforeDescription, service.Description)
            )
        }, ct);

        return TypedResults.NoContent();
    }
}
