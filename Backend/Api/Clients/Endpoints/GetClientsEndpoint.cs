using Backend.Api.Base.Models;
using Backend.Api.Clients.Models;
using Backend.Data;
using Backend.Data.Entities;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Clients.Endpoints;

public class GetClientsEndpoint(AppDbContext db)
    : Endpoint<PaginationRequest, Results<Ok<PaginatedResponse<ClientWithBalanceResponse>>, NotFound, ProblemDetails>>
{
    public override void Configure()
    {
        Get("/api/clients");
    }

    public override async Task<Results<Ok<PaginatedResponse<ClientWithBalanceResponse>>, NotFound, ProblemDetails>> ExecuteAsync(
        PaginationRequest req,
        CancellationToken ct)
    {
        var skipped = req.PageSize * (req.Page - 1);
        
        // Get clients with their contacts
        var clients = await db.Clients
            .Include(e => e.Contacts)
            .Skip(skipped)
            .Take(req.PageSize)
            .ToListAsync(ct);

        // Get total count
        var clientsCount = await db.Clients.CountAsync(cancellationToken: ct);

        // Calculate balances for each client
        var clientBalances = new List<ClientWithBalanceResponse>();
        foreach (var client in clients)
        {
            // Get total payments
            var totalPayments = await db.Payments
                .Where(p => p.Client.Id == client.Id)
                .SumAsync(p => p.Amount, ct);

            // Get total service costs
            var totalServiceCosts = await db.Schedule
                .Where(s => s.Client.Id == client.Id && s.Completed)
                .Join(db.ServicePriceHistories,
                    s => s.Service.Id,
                    p => p.Service.Id,
                    (s, p) => p.Price)
                .SumAsync(ct);

            var balance = totalPayments - totalServiceCosts;

            clientBalances.Add(new ClientWithBalanceResponse
            {
                Id = client.Id,
                FirstName = client.FirstName,
                LastName = client.LastName,
                Patronymic = client.Patronymic,
                Contacts = client.Contacts,
                Balance = balance
            });
        }

        return TypedResults.Ok(PaginatedResponse<ClientWithBalanceResponse>.Create(
            clientBalances, 
            clientsCount, 
            req.Page, 
            req.PageSize,
            skipped));
    }
}