using FastEndpoints;
using MelodyTrack.Backend.Api.ClientSources.Requests;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MelodyTrack.Backend.Api.ClientSources.Endpoints;

public class CreateClientSourceEndpoint(
    AppDbContext db,
    IAuditLogService auditLogService,
    IRequestReplayService requestReplayService)
    : Ep.Req<CreateClientSourceRequest>.Res<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult>>
{
    private const string ReplayEndpoint = "client-sources:create";

    public override void Configure()
    {
        Post("/client-sources");
    }

    public override async Task<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult>> ExecuteAsync(
        CreateClientSourceRequest req,
        CancellationToken ct)
    {
        var replayKey = requestReplayService.GetReplayKey(HttpContext.Request.Headers);
        if (replayKey is not null)
        {
            var existingId = await requestReplayService.TryGetResponseEntityIdAsync(ReplayEndpoint, replayKey, ct);
            if (existingId is not null)
            {
                return TypedResults.Created($"/client-sources/{existingId}", new CreateEntityResponse
                {
                    Id = existingId.Value
                });
            }
        }

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        RequestReplay? replay = null;

        try
        {
            if (replayKey is not null)
            {
                transaction = await db.Database.BeginTransactionAsync(ct);
                replay = await requestReplayService.ReserveAsync(ReplayEndpoint, replayKey, ct);
            }

            var source = new ClientSource
            {
                Id = Ulid.NewUlid(),
                Name = req.Name.Trim()
            };

            await db.ClientSources.AddAsync(source, ct);
            await db.SaveChangesAsync(ct);

            await auditLogService.WriteAsync(new AuditLogWriteRequest
            {
                Category = "clients",
                Action = "client_source_created",
                EntityType = "client_source",
                EntityId = source.Id.ToString(),
                Details = source.Name
            }, ct);

            if (replay is not null)
            {
                await requestReplayService.CompleteAsync(replay, source.Id, ct);
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(ct);
            }

            return TypedResults.Created($"/client-sources/{source.Id}", new CreateEntityResponse
            {
                Id = source.Id
            });
        }
        catch (DbUpdateException ex) when (replayKey is not null && IsUniqueViolation(ex))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(ct);
            }

            var completedId = await requestReplayService.WaitForResponseEntityIdAsync(ReplayEndpoint, replayKey, ct);
            if (completedId is not null)
            {
                return TypedResults.Created($"/client-sources/{completedId}", new CreateEntityResponse
                {
                    Id = completedId.Value
                });
            }

            throw;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    }
}
