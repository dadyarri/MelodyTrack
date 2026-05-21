using Facet.Mapping;
using FastEndpoints;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class GetClientsWithNegativeBalanceEndpoint(AppDbContext db, ClientToClientWithBalanceDtoMapConfig mapper, IRecordActivityService recordActivityService)
    : Ep.NoReq.Res<Results<Ok<GetClientsWithNegativeBalanceResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Get("/clients/inDebt");
    }

    public override async Task<Results<Ok<GetClientsWithNegativeBalanceResponse>, UnauthorizedHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var clients = await db.Clients
            .AsNoTracking()
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .Include(e => e.Contacts)
            .Include(e => e.Source)
            .ToListAsync(ct);

        var clientsFacets = await clients.ToFacetsAsync(mapper, ct);
        var clientActivities = await recordActivityService.GetLatestActivitiesAsync(
            "client",
            clientsFacets.Select(client => client.Id.ToString()).ToList(),
            ct);

        foreach (var client in clientsFacets)
        {
            if (clientActivities.TryGetValue(client.Id.ToString(), out var activity))
            {
                client.LastActivity = activity;
            }
        }

        var debtors = clientsFacets.Where(e => e.Balance < 0).ToList();

        return TypedResults.Ok(new GetClientsWithNegativeBalanceResponse
        {
            Debtors = debtors
        });
    }
}
