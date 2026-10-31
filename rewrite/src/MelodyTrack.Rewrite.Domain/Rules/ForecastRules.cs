using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class MonthlyForecastRules
{
    public static Result<MonthlyForecast, ErrorList> Create(int year, int month, IEnumerable<Appointment> appointments, IEnumerable<Service> services, IEnumerable<AppointmentSettlement> settlements, DateTimeOffset now, IEnumerable<AppointmentId> plannedSubscriptionAppointments)
    {
        if (year is < 1 or > 9999 || month is < 1 or > 12) return ErrorList.Single(new ModelError.InvalidValue(), "month");
        var catalogue = services.DistinctBy(item => item.Id).ToDictionary(item => item.Id);
        var charges = settlements.Where(item => !item.IsReversed).ToArray();
        var covered = plannedSubscriptionAppointments.ToHashSet();
        var lines = ImmutableArray.CreateBuilder<ForecastLine>();
        foreach (var appointment in appointments.Where(item => !item.IsDeleted && item.Status != AppointmentStatus.Cancelled).DistinctBy(item => item.Id))
        {
            var charge = charges.Where(item => item.AppointmentId == appointment.Id).ToArray();
            if (charge.Length > 1) return ErrorList.Single(new FinanceError.AlreadySettled());
            if (charge.FirstOrDefault()?.Source is SettlementSource.Cash cash)
                lines.Add(new(appointment.Id, cash.Amount, null));
            else if (charge.FirstOrDefault()?.Source is SettlementSource.Subscription || covered.Contains(appointment.Id))
                lines.Add(new(appointment.Id, null, ForecastUncertainty.SubscriptionValuation));
            else if (appointment.Status == AppointmentStatus.Scheduled && appointment.EndsAt <= now)
                lines.Add(new(appointment.Id, null, ForecastUncertainty.PastAppointmentWithoutResult));
            else if (!catalogue.TryGetValue(appointment.ServiceId, out var service) || service.PriceAt(appointment.EndsAt) is not { } price)
                lines.Add(new(appointment.Id, null, ForecastUncertainty.MissingPrice));
            else
            {
                var amount = Money.Create(price);
                if (amount.IsFailure) return amount.Error;
                lines.Add(new(appointment.Id, amount.Value, null));
            }
        }
        return new MonthlyForecast(year, month, lines.ToImmutable());
    }

    public static decimal KnownValue(this MonthlyForecast model) => model.Lines.Sum(line => line.Value?.Amount ?? 0);

    public static bool IsComplete(this MonthlyForecast model) => model.Lines.All(line => line.Uncertainty is null);
}
