using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class DeleteClientEndpoint(AppDbContext db): Ep.Req<GetEntityRequest>.Res<Results<NoContent, NotFound>>
{
    public override void Configure()
    {
        Delete("/client/{id}");
    }

    public override async Task<Results<NoContent, NotFound>> ExecuteAsync(GetEntityRequest req, CancellationToken ct)
    {
        var rowsDeleted = await db.Clients.Where(e => e.Id == req.Id).ExecuteDeleteAsync(ct);

        if (rowsDeleted == 0)
        {
            return TypedResults.NotFound();
        }
        
        return TypedResults.NoContent();
    }
}