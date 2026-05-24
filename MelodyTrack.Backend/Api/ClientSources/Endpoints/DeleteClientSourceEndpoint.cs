using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.ClientSources.Endpoints;

public class DeleteClientSourceEndpoint(AppDbContext db, IAuditLogService auditLogService)
    : Ep.Req<GetEntityRequest>.Res<Results<NoContent, NotFound<ProblemDetails>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Delete("/client-sources/{id}");
    }

    public override async Task<Results<NoContent, NotFound<ProblemDetails>, UnauthorizedHttpResult>> ExecuteAsync(
        GetEntityRequest req,
        CancellationToken ct)
    {
        var source = await db.ClientSources
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == req.Id, ct);

        if (source is null)
        {
            return TypedResults.NoContent();
        }

        await db.Clients
            .Where(e => e.SourceId == req.Id)
            .ExecuteUpdateAsync(updates => updates.SetProperty(client => client.SourceId, _ => null), ct);

        await db.ClientSources
            .Where(e => e.Id == req.Id)
            .ExecuteDeleteAsync(ct);

        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "clients",
            Action = "client_source_deleted",
            EntityType = "client_source",
            EntityId = source.Id.ToString(),
            Details = AuditDetailsFormatter.DescribeContext("Источник клиента", source.Name)
        }, ct);

        return TypedResults.NoContent();
    }
}
