using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Clients.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

[ApiEndpoint(ApiMethod.Patch, "/clients/{id}/lead-status")]
public sealed class SetLeadStatusEndpoint
{

    [Microsoft.AspNetCore.Authorization.Authorize(Policy = MelodyTrack.Backend.Api.Auth.AuthorizationPolicies.Administrator)]
    public static async Task<Results<NoContent, NotFound<ApiProblemDetails>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        SetLeadStatusRequest req,
        Ulid id,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        IAuditLogService auditLogService,
        HttpContext httpContext,
        ApiValidationErrorCollection validationErrors,
        CancellationToken ct
    )
    {
        req.Id = id;
        var role = (await currentUserAccessor.GetAsync(ct))?.Role.RoleName;
        if (role is null) return TypedResults.Unauthorized();
        if (!role.Value.IsAnyAdmin()) return TypedResults.Forbid();

        var client = await db.Clients.FirstOrDefaultAsync(item => item.Id == req.Id, ct);
        if (client is null)
        {
            validationErrors.Add(nameof(req.Id), "Клиент не найден");
            return TypedResults.NotFound(new ApiProblemDetails(validationErrors, httpContext, StatusCodes.Status404NotFound));
        }

        if (client.IsLeadClosed == req.IsClosed) return TypedResults.NoContent();

        client.IsLeadClosed = req.IsClosed;
        await db.SaveChangesAsync(ct);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Event = req.IsClosed
                ? MelodyTrack.Core.Auditing.AuditCatalog.Events.LeadClosed
                : MelodyTrack.Core.Auditing.AuditCatalog.Events.LeadReopened,
            EntityType = "client",
            EntityId = client.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Клиент", $"{client.LastName} {client.FirstName}".Trim()),
                AuditDetailsFormatter.DescribeContext("Статус лида", req.IsClosed ? "Закрыт" : "Открыт"))
        }, ct);

        return TypedResults.NoContent();
    }
}
