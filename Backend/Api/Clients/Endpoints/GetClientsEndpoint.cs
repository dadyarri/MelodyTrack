using Backend.Api.Base.Models;
using Backend.Data;
using Backend.Data.Entities;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Clients.Endpoints;

public class GetClientsEndpoint(AppDbContext db)
    : Endpoint<PaginationRequest, Results<Ok<PaginatedResponse<Client>>, NotFound, ProblemDetails>>
{
    public override void Configure()
    {
        Get("/api/clients");
    }

    public override async Task<Results<Ok<PaginatedResponse<Client>>, NotFound, ProblemDetails>> ExecuteAsync(
        PaginationRequest req,
        CancellationToken ct)
    {
        var skipped = req.PageSize * (req.Page - 1);
        var clients = await db.Clients
            .Include(e => e.Contacts)
            .Skip(skipped)
            .Take(req.PageSize)
            .ToListAsync(ct);

        var clientsCount = await db.Clients.CountAsync(cancellationToken: ct);

        return TypedResults.Ok(PaginatedResponse<Client>.Create(clients, clientsCount, req.Page, req.PageSize,
            skipped));
    }
}