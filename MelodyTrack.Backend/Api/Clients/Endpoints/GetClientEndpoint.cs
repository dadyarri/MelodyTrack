using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class GetClientEndpoint(AppDbContext db) : Ep.Req<GetEntityRequest>.Res<Results<Ok<Client>, NotFound>>
{
    public override void Configure()
    {
        Get("/clients/{id}");
    }

    public override async Task<Results<Ok<Client>, NotFound>> ExecuteAsync(GetEntityRequest req, CancellationToken ct)
    {
        var client = await db.Clients.FirstOrDefaultAsync(e => e.Id == req.Id, ct);

        if (client is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(client);
    }
}