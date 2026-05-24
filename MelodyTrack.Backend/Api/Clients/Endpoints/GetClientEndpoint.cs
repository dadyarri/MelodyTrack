using Facet.Mapping;
using FastEndpoints;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class GetClientEndpoint(AppDbContext db, ClientToClientWithBalanceDtoMapConfig mapper, IRecordActivityService recordActivityService)
    : Ep.Req<GetEntityRequest>.Res<Results<Ok<ClientWithBalanceDto>, NotFound>>
{
    public override void Configure()
    {
        Get("/clients/{id}");
    }

    public override async Task<Results<Ok<ClientWithBalanceDto>, NotFound>> ExecuteAsync(GetEntityRequest req, CancellationToken ct)
    {
        Logger.LogDebug("Fetching client with ID: {ClientId}", req.Id);
        var client = await db.Clients
            .AsNoTracking()
            .Include(e => e.Contacts)
            .Include(e => e.Source)
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
