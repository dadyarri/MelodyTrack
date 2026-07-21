using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Services.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Services.Endpoints;

public class UpdateServiceEndpoint(AppDbContext db, IAuditLogService auditLogService, IEntityFreshnessService entityFreshnessService)
    : Ep.Req<UpdateServiceRequest>.Res<Results<NoContent, NotFound<ProblemDetails>, UnauthorizedHttpResult, ForbidHttpResult, Conflict<StaleEntityConflictResponse>>>
{
    public override void Configure()
    {
        Put("/services/{id}");
    }

    public override async Task<Results<NoContent, NotFound<ProblemDetails>, UnauthorizedHttpResult, ForbidHttpResult, Conflict<StaleEntityConflictResponse>>> ExecuteAsync(
        UpdateServiceRequest req,
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
            && (req.Name != service.Name || req.Description != service.Description || req.IsConsultation != service.IsConsultation))
        {
            return TypedResults.Conflict(conflict);
        }

        var beforeName = service.Name;
        var beforeDescription = service.Description;
        var beforeIsConsultation = service.IsConsultation;

        service.Name = req.Name;
        service.Description = req.Description;
        service.IsConsultation = req.IsConsultation;

        await db.SaveChangesAsync(ct);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "services",
            Action = "service_updated",
            EntityType = "service",
            EntityId = service.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeChange("Название", beforeName, service.Name),
                AuditDetailsFormatter.DescribeChange("Описание", beforeDescription, service.Description),
                AuditDetailsFormatter.DescribeChange("Консультация", beforeIsConsultation ? "Да" : "Нет", service.IsConsultation ? "Да" : "Нет")
            )
        }, ct);

        return TypedResults.NoContent();
    }
}
