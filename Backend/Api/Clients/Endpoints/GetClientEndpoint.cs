using Backend.Api.Base.Models;
using Backend.Api.Clients.Models;
using Backend.Data;
using Backend.Data.Entities;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Clients.Endpoints;

public class GetClientEndpoint(AppDbContext db)
    : Endpoint<GetEntityRequest, Results<Ok<GetClientResponse>, NotFound, ProblemDetails>>
{
    public override void Configure()
    {
        Get("/api/clients/{id:long}");
    }

    public override async Task<Results<Ok<GetClientResponse>, NotFound, ProblemDetails>> ExecuteAsync(
        GetEntityRequest req,
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

        var payments = await db.Payments
            .Include(e => e.Client)
            .Where(e => e.Client.Id == req.Id)
            .OrderByDescending(e => e.Date)
            .Take(5)
            .ToListAsync(ct);

        var clientDto = new GetClientResponse
        {
            FirstName = client.FirstName,
            LastName = client.LastName,
            Patronymic = client.Patronymic,
            Contacts = client.Contacts,
            LatestPayments = payments
        };

        return TypedResults.Ok(clientDto);
    }
}