using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.Mvc;
using Facet.Mapping;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/clients/{id}")]
public sealed class GetClientEndpoint
{

    [Microsoft.AspNetCore.Authorization.Authorize(Policy = MelodyTrack.Backend.Api.Auth.AuthorizationPolicies.Administrator)]
    public static async Task<Results<Ok<ClientWithBalanceDto>, UnauthorizedHttpResult, ForbidHttpResult, NotFound>> HandleAsync(
        [AsParameters] GetEntityRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        ClientToClientWithBalanceDtoMapConfig mapper,
        IRecordActivityService recordActivityService,
        ILogger<GetClientEndpoint> logger,
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

        logger.LogDebug("Fetching client with ID: {ClientId}", req.Id);
        var client = await db.Clients
            .AsNoTracking()
            .Include(e => e.Contacts)
            .Include(e => e.Source)
            .Include(e => e.Vacations)
            .FirstOrDefaultAsync(e => e.Id == req.Id, ct);

        if (client is null)
        {
            logger.LogWarning("Client with ID {ClientId} not found", req.Id);
            return TypedResults.NotFound();
        }

        var clientDto = (await new[] { client }.ToList().ToFacetsAsync(mapper, ct)).Single();
        clientDto.LastActivity = await recordActivityService.GetLatestActivityAsync("client", client.Id.ToString(), ct);

        logger.LogDebug("Successfully retrieved client {FirstName} {LastName} (ID: {ClientId})",
            client.FirstName, client.LastName, client.Id);
        return TypedResults.Ok(clientDto);
    }
}
