using Backend.Api.Clients.Models;
using Backend.Data;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Clients.Endpoints;

/// <summary>
/// Получить список клиентов с долгом
/// </summary>
/// <param name="db">БД</param>
public class GetClientsWithNegativeBalanceEndpoint(AppDbContext db)
    : Endpoint<EmptyRequest, Ok<List<ClientWithBalanceResponse>>>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Get("/api/clients/in-debt");
    }

    /// <inheritdoc />
    public override async Task<Ok<List<ClientWithBalanceResponse>>> ExecuteAsync(EmptyRequest req, CancellationToken ct)
    {
        var allClients = await db.Clients
            .Include(e => e.Contacts)
            .ToListAsync(ct);

        var clientsWithBalances = new List<ClientWithBalanceResponse>();

        foreach (var client in allClients)
        {
            var totalPayments = await db.Payments
                .Where(p => p.Client.Id == client.Id)
                .SumAsync(p => p.Amount, ct);

            var totalServiceCosts = await db.Schedule
                .Where(s => s.Client.Id == client.Id && s.Completed)
                .Join(db.ServicePriceHistories,
                    s => s.Service.Id,
                    p => p.Service.Id,
                    (s, p) => p.Price)
                .SumAsync(ct);

            var balance = totalPayments - totalServiceCosts;

            clientsWithBalances.Add(new ClientWithBalanceResponse
            {
                Id = client.Id,
                FirstName = client.FirstName,
                LastName = client.LastName,
                Patronymic = client.Patronymic,
                Contacts = client.Contacts,
                Balance = balance
            });
        }

        var clientsWithNegativeBalance = clientsWithBalances
            .Where(c => c.Balance < 0)
            .OrderBy(c => c.Id)
            .ToList();

        return TypedResults.Ok(clientsWithNegativeBalance);
    }
}