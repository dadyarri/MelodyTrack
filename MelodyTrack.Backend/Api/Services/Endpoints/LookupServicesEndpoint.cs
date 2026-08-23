using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api.Services.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Services.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/services/options")]
public sealed class LookupServicesEndpoint
{

    [Microsoft.AspNetCore.Authorization.Authorize(Policy = MelodyTrack.Backend.Api.Auth.AuthorizationPolicies.Administrator)]
    public static async Task<Results<Ok<LookupServicesResponse>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        ILogger<LookupServicesEndpoint> logger,
        CancellationToken ct
    )
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

        logger.LogDebug("Fetching lookup list of all services");
        var services = await db.Services
            .AsNoTracking()
            .Select(service => new LookupServicesDto
            {
                Id = service.Id,
                Name = service.Name,
                Price = db.ServicePriceHistory
                    .Where(price => price.Service.Id == service.Id)
                    .OrderByDescending(price => price.EffectiveDate)
                    .Select(price => (decimal?)price.Price)
                    .FirstOrDefault() ?? 0m
            })
            .OrderBy(e => e.Name)
            .ToListAsync(ct);

        logger.LogInformation("Retrieved {Count} services for lookup list", services.Count);

        return TypedResults.Ok(new LookupServicesResponse
        {
            Services = services
        });
    }
}
