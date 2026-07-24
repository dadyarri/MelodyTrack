using FastEndpoints;
using MelodyTrack.Backend.Api.ClientSources.Requests;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.ClientSources.Endpoints;

public class CreateClientSourceEndpoint(
    AppDbContext db, ICurrentUserAccessor currentUserAccessor,
    IAuditLogService auditLogService,
    IRequestReplayService requestReplayService)
    : Ep.Req<CreateClientSourceRequest>.Res<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, ForbidHttpResult>>
{
    private const string ReplayEndpoint = "client-sources:create";

    public override void Configure()
    {
        Post("/client-sources");
    }

    public override async Task<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(
        CreateClientSourceRequest req,
        CancellationToken ct)
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

        var replayKey = requestReplayService.GetReplayKey(HttpContext.Request.Headers);
        await using var transaction = replayKey is null ? null : await db.Database.BeginTransactionAsync(ct);
        Ulid? reservationId = null;
        if (replayKey is not null)
        {
            var decision = await requestReplayService.AcquireAsync(ReplayEndpoint, replayKey, req, ct);
            if (decision.Status == RequestReplayStatus.Completed)
            {
                return TypedResults.Created($"/client-sources/{decision.ResponseEntityId}", new CreateEntityResponse
                {
                    Id = decision.ResponseEntityId!.Value
                });
            }

            reservationId = decision.ReservationId;
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
            Details = AuditDetailsFormatter.DescribeContext("Источник клиента", source.Name)
        }, ct);

        if (reservationId is not null)
        {
            await requestReplayService.CompleteAsync(reservationId.Value, source.Id, ct);
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
}
