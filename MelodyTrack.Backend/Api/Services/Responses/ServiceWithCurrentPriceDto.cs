using Facet;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Services.Responses;

[Facet(typeof(Service))]
public partial class ServiceWithCurrentPriceDto
{
    public decimal Price { get; set; }
    public RecordActivityDto? LastActivity { get; set; }
}

public sealed class ServiceWithCurrentPriceDtoMapper(AppDbContext db)
{
    public async Task<List<ServiceWithCurrentPriceDto>> MapAsync(
        IReadOnlyCollection<Service> services,
        CancellationToken cancellationToken = default)
    {
        if (services.Count == 0)
        {
            return [];
        }

        var serviceIds = services.Select(service => service.Id).ToArray();
        var currentPrices = await db.Services
            .AsNoTracking()
            .Where(service => serviceIds.Contains(service.Id))
            .Select(service => new ServiceCurrentPrice(
                service.Id,
                db.ServicePriceHistory
                    .Where(price => price.Service.Id == service.Id)
                    .OrderByDescending(price => price.EffectiveDate)
                    .Select(price => (decimal?)price.Price)
                    .FirstOrDefault() ?? 0m))
            .ToDictionaryAsync(price => price.ServiceId, price => price.Price, cancellationToken);

        return services.Select(service =>
        {
            var target = new ServiceWithCurrentPriceDto(service)
            {
                Price = currentPrices.GetValueOrDefault(service.Id)
            };
            return target;
        }).ToList();
    }

    private sealed record ServiceCurrentPrice(Ulid ServiceId, decimal Price);
}
