using FastEndpoints;
using MelodyTrack.Backend.Api.Clients.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class SetLeadStatusEndpoint(AppDbContext db, IAuditLogService auditLogService)
    : Ep.Req<SetLeadStatusRequest>.Res<Results<NoContent, NotFound<ProblemDetails>, UnauthorizedHttpResult, ForbidHttpResult>>
{
    public override void Configure() => Patch("/clients/{id}/lead-status");

    public override async Task<Results<NoContent, NotFound<ProblemDetails>, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(SetLeadStatusRequest req, CancellationToken ct)
    {
        var role = await EndpointAuthUtils.GetCurrentUserRoleAsync(User, db, ct);
        if (role is null) return TypedResults.Unauthorized();
        if (!role.Value.IsAnyAdmin()) return TypedResults.Forbid();

        var client = await db.Clients.FirstOrDefaultAsync(item => item.Id == req.Id, ct);
        if (client is null)
        {
            AddError(item => item.Id, "Клиент не найден");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        if (client.IsLeadClosed == req.IsClosed) return TypedResults.NoContent();

        client.IsLeadClosed = req.IsClosed;
        await db.SaveChangesAsync(ct);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "clients",
            Action = req.IsClosed ? "lead_closed" : "lead_reopened",
            EntityType = "client",
            EntityId = client.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Клиент", $"{client.LastName} {client.FirstName}".Trim()),
                AuditDetailsFormatter.DescribeContext("Статус лида", req.IsClosed ? "Закрыт" : "Открыт"))
        }, ct);

        return TypedResults.NoContent();
    }
}
