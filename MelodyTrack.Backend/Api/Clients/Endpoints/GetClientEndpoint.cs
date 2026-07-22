using Facet.Mapping;
using FastEndpoints;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class GetClientEndpoint(AppDbContext db, ClientToClientWithBalanceDtoMapConfig mapper, IRecordActivityService recordActivityService)
    : Ep.Req<GetEntityRequest>.Res<Results<Ok<ClientWithBalanceDto>, UnauthorizedHttpResult, ForbidHttpResult, NotFound>>
{
    public override void Configure()
    {
        Get("/clients/{id}");
    }

    public override async Task<Results<Ok<ClientWithBalanceDto>, UnauthorizedHttpResult, ForbidHttpResult, NotFound>> ExecuteAsync(GetEntityRequest req, CancellationToken ct)
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

        Logger.LogDebug("Fetching client with ID: {ClientId}", req.Id);
        var client = await db.Clients
            .AsNoTracking()
            .Include(e => e.Contacts)
            .Include(e => e.Source)
            .Include(e => e.Vacations)
            .FirstOrDefaultAsync(e => e.Id == req.Id, ct);

        if (client is null)
        {
            Logger.LogWarning("Client with ID {ClientId} not found", req.Id);
            return TypedResults.NotFound();
        }

        var clientDto = (await new[] { client }.ToList().ToFacetsAsync(mapper, ct)).Single();
        clientDto.LastActivity = await recordActivityService.GetLatestActivityAsync("client", client.Id.ToString(), ct);

        Logger.LogDebug("Successfully retrieved client {FirstName} {LastName} (ID: {ClientId})",
            client.FirstName, client.LastName, client.Id);
        return TypedResults.Ok(clientDto);
    }
}
