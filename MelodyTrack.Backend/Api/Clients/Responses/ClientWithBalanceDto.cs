using Facet;
using Facet.Mapping;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Responses;

[Facet(typeof(Client), exclude: nameof(Client.Contacts))]
public partial class ClientWithBalanceDto
{
    public decimal Balance { get; set; }
}

public class ClientToClientWithBalanceDtoMapConfig(AppDbContext db)
    : IFacetMapConfigurationAsyncInstance<Client, ClientWithBalanceDto>
{
    public async Task MapAsync(Client source, ClientWithBalanceDto target,
        CancellationToken cancellationToken = default)
    {
        var totalPayments = await db.Payments
            .Where(e => e.Client.Id == source.Id)
            .SumAsync(e => e.Amount, cancellationToken);

        var totalServiceCost = await db.Appointments
            .Where(e => e.Client.Id == source.Id && e.IsCompleted)
            .Join(db.ServicePriceHistory, s => s.Service.Id, p => p.Service.Id, (s, p) => p.Price)
            .SumAsync(cancellationToken);

        target.Balance = totalPayments - totalServiceCost;
    }
}