using Facet.Extensions;
using FastEndpoints;
using MelodyTrack.Backend.Api.Services.Responses;
using MelodyTrack.Backend.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Services.Endpoints;

public class LookupServicesEndpoint(AppDbContext db)
    : Ep.NoReq.Res<Results<Ok<LookupServicesResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Get("/services/lookup");
    }

    public override async Task<Results<Ok<LookupServicesResponse>, UnauthorizedHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        Logger.LogDebug("Fetching lookup list of all services");
        var services = await db.Services
            .SelectFacet<LookupServicesDto>()
            .OrderBy(e => e.Name)
            .ToListAsync(ct);

        Logger.LogInformation("Retrieved {Count} services for lookup list", services.Count);

        return TypedResults.Ok(new LookupServicesResponse
        {
            Services = services
        });
    }
}