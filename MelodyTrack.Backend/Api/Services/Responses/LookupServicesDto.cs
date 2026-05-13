using Facet;
using Facet.Mapping;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Services.Responses;

[Facet(typeof(Service), Include = [nameof(Service.Id), nameof(Service.Name)])]
public partial class LookupServicesDto
{
    public decimal Price { get; set; }
}

public class ServiceToLookupServicesDtoMapConfig(AppDbContext db)
    : IFacetMapConfigurationAsyncInstance<Service, LookupServicesDto>
{
    public async Task MapAsync(Service source, LookupServicesDto target, CancellationToken cancellationToken = default)
    {
        var latestPrice = await db.ServicePriceHistory
            .Where(item => item.Service == source)
            .OrderByDescending(item => item.EffectiveDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestPrice is not null)
        {
            target.Price = latestPrice.Price;
        }
    }
}
