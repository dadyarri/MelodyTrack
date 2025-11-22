using Facet.Extensions;
using FastEndpoints;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Api.Services.Responses;
using MelodyTrack.Common.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Services.Endpoints;

public class LookupServicesEndpoint(AppDbContext db)
    : Ep.NoReq.Res<IResult>
{
    public override void Configure()
    {
        Get("/services/lookup");
    }

    public override async Task<IResult> ExecuteAsync(
        CancellationToken ct)
    {
        Logger.LogDebug("Fetching lookup list of all services");
        var services = await db.Services
            .AsNoTracking()
            .SelectFacet<LookupServicesDto>()
            .OrderBy(e => e.Name)
            .ToListAsync(ct);

        Logger.LogInformation("Retrieved {Count} services for lookup list", services.Count);

        return ApiResults.Ok(new LookupServicesResponse
        {
            Services = services
        });
    }
}