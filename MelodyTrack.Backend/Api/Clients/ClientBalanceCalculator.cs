using MelodyTrack.Backend.Api.Dashboard;

namespace MelodyTrack.Backend.Api.Clients;

internal static class ClientBalanceCalculator
{
    public static decimal CalculateServiceCost(
        IEnumerable<(Ulid ServiceId, DateTime StartDateUtc)> appointments,
        IReadOnlyDictionary<Ulid, List<ServicePriceSnapshot>> priceLookup)
    {
        return appointments.Sum(appointment =>
            DashboardPriceResolver.ResolveAppointmentPrice(
                appointment.ServiceId,
                appointment.StartDateUtc,
                priceLookup,
                price => price.EffectiveDateUtc,
                price => price.Price));
    }
}

internal sealed record ServicePriceSnapshot(DateTime EffectiveDateUtc, decimal Price);
