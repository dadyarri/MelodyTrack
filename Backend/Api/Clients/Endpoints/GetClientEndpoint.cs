using Backend.Api.Base.Models;
using Backend.Data;
using Backend.Data.Entities;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Clients.Endpoints;

public class GetClientEndpoint(AppDbContext db)
    : Endpoint<GetEntityRequest, Results<Ok<Client>, NotFound, ProblemDetails>>
{
    public override void Configure()
    {
        Get("/api/clients/{id:long}");
        AllowAnonymous();
    }

    public override async Task<Results<Ok<Client>, NotFound, ProblemDetails>> ExecuteAsync(GetEntityRequest req,
        CancellationToken ct)
    {
        var client = await db.Clients
            .Where(e => e.Id == req.Id)
            .Include(e => e.Contacts)
            .FirstOrDefaultAsync(ct);

        if (client == null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(client);
    }
}