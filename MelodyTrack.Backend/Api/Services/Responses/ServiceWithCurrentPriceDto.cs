using Facet;
using Facet.Mapping;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Services.Responses;

[Facet(typeof(Service))]
public partial class ServiceWithCurrentPriceDto
{
    public decimal Price { get; set; }
}

public class ServiceToServiceWithCurrentPriceDtoMapConfig(AppDbContext db)
    : IFacetMapConfigurationAsyncInstance<Service, ServiceWithCurrentPriceDto>
{
    public async Task MapAsync(Service source, ServiceWithCurrentPriceDto target,
        CancellationToken cancellationToken = default)
    {
        var latestPrice = await db.ServicePriceHistory
            .Where(e => e.Service == source)
            .OrderByDescending(e => e.EffectiveDate)
            .Take(1)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestPrice is not null)
        {
            target.Price = latestPrice.Price;
        }
    }
}