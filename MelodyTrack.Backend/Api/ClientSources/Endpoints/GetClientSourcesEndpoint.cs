using FastEndpoints;
using MelodyTrack.Backend.Api.ClientSources.Responses;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.ClientSources.Endpoints;

public class GetClientSourcesEndpoint(AppDbContext db)
    : Ep.NoReq.Res<Results<Ok<GetClientSourcesResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Get("/client-sources");
    }

    public override async Task<Results<Ok<GetClientSourcesResponse>, UnauthorizedHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var sources = await db.ClientSources
            .AsNoTracking()
            .OrderBy(e => e.Name)
            .Select(e => new ReferenceBookItemDto
            {
                Id = e.Id,
                Name = e.Name
            })
            .ToListAsync(ct);

        return TypedResults.Ok(new GetClientSourcesResponse
        {
            Sources = sources
        });
    }
}
