using Backend.Api.Base.Models;
using Backend.Data;
using Backend.Data.Entities;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Clients.Endpoints;

public class GetClientsEndpoint(AppDbContext db)
    : Endpoint<GetEntityRequest, Results<Ok<List<Client>>, NotFound, ProblemDetails>>
{
    public override void Configure()
    {
        Get("/api/clients");
        AllowAnonymous();
    }

    public override async Task<Results<Ok<List<Client>>, NotFound, ProblemDetails>> ExecuteAsync(GetEntityRequest req,
        CancellationToken ct)
    {
        var clients = await db.Clients
            .Include(e => e.Contacts)
            .ToListAsync(ct);

        return TypedResults.Ok(clients);
    }
}