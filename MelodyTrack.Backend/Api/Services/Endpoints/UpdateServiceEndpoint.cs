using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Services.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Services.Endpoints;

[ApiEndpoint(ApiMethod.Patch, "/services/{id}")]
public sealed class UpdateServiceEndpoint
{

    public static async Task<Results<NoContent, NotFound<ApiProblemDetails>, UnauthorizedHttpResult, ForbidHttpResult, Conflict<StaleEntityConflictResponse>>> HandleAsync(
        UpdateServiceRequest req,
        Ulid id,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        IAuditLogService auditLogService,
        IEntityFreshnessService entityFreshnessService,
        HttpContext httpContext,
        ApiValidationErrorCollection validationErrors,
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

        var service = await db.Services.FirstOrDefaultAsync(item => item.Id == req.Id, ct);
        if (service is null)
        {
            validationErrors.Add(nameof(req.Id), "Услуга не найдена");
            return TypedResults.NotFound(new ApiProblemDetails(validationErrors, httpContext, StatusCodes.Status404NotFound));
        }

        var conflict = await entityFreshnessService.GetConflictIfStaleAsync(
            "service",
            service.Id,
            req.ExpectedActivityId,
            "Услуга была изменена другим пользователем. Обновите данные и повторите сохранение.",
            ct);

        if (conflict is not null
            && (req.Name != service.Name || req.PublicName != service.PublicName || req.Description != service.Description || req.IsConsultation != service.IsConsultation))
        {
            return TypedResults.Conflict(conflict);
        }

        var beforeName = service.Name;
        var beforeDescription = service.Description;
        var beforeIsConsultation = service.IsConsultation;

        service.Name = req.Name;
        service.PublicName = req.PublicName;
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
                AuditDetailsFormatter.DescribeContext("Услуга", service.Name),
                AuditDetailsFormatter.DescribeChange("Название", beforeName, service.Name),
                AuditDetailsFormatter.DescribeChange("Описание", beforeDescription, service.Description),
                AuditDetailsFormatter.DescribeChange("Консультация", beforeIsConsultation ? "Да" : "Нет", service.IsConsultation ? "Да" : "Нет")
            )
        }, ct);

        return TypedResults.NoContent();
    }
}
