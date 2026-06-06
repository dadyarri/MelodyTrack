using FastEndpoints;
using MelodyTrack.Backend.Api.ClientSources.Responses;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.ClientSources.Endpoints;

public class GetClientSourcesEndpoint(AppDbContext db, IRecordActivityService recordActivityService)
    : Ep.NoReq.Res<Results<Ok<GetClientSourcesResponse>, UnauthorizedHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/client-sources");
    }

    public override async Task<Results<Ok<GetClientSourcesResponse>, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(CancellationToken ct)
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

        var sources = await db.ClientSources
            .AsNoTracking()
            .OrderBy(e => e.Name)
            .Select(e => new ReferenceBookItemDto
            {
                Id = e.Id,
                Name = e.Name
            })
            .ToListAsync(ct);

        var latestActivities = await recordActivityService.GetLatestActivitiesAsync(
            "client_source",
            sources.Select(source => source.Id.ToString()).ToArray(),
            ct);

        foreach (var source in sources)
        {
            source.LastActivity = latestActivities.GetValueOrDefault(source.Id.ToString());
        }

        return TypedResults.Ok(new GetClientSourcesResponse
        {
            Sources = sources
        });
    }
}
