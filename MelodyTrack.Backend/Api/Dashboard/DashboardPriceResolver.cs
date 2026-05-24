namespace MelodyTrack.Backend.Api.Dashboard;

internal static class DashboardPriceResolver
{
    public static decimal ResolveAppointmentPrice<TPriceRow>(
        Ulid serviceId,
        DateTime appointmentStartDateUtc,
        IReadOnlyDictionary<Ulid, List<TPriceRow>> priceLookup,
        Func<TPriceRow, DateTime> effectiveDateSelector,
        Func<TPriceRow, decimal> priceSelector)
    {
        if (!priceLookup.TryGetValue(serviceId, out var prices) || prices.Count == 0)
        {
            return 0m;
        }

        TPriceRow? earliestPrice = default;
        DateTime? earliestEffectiveDate = null;
        TPriceRow? resolvedPrice = default;
        DateTime? resolvedEffectiveDate = null;

        foreach (var price in prices)
        {
            var effectiveDate = effectiveDateSelector(price);

            if (earliestEffectiveDate is null || effectiveDate < earliestEffectiveDate.Value)
            {
                earliestEffectiveDate = effectiveDate;
                earliestPrice = price;
            }

            if (effectiveDate <= appointmentStartDateUtc
                && (resolvedEffectiveDate is null || effectiveDate > resolvedEffectiveDate.Value))
            {
                resolvedEffectiveDate = effectiveDate;
                resolvedPrice = price;
            }
        }

        if (resolvedEffectiveDate is not null)
        {
            return priceSelector(resolvedPrice!);
        }

        return priceSelector(earliestPrice!);
    }
}
