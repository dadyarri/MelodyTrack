using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class MonthlyForecastRules
{
    public static Result<MonthlyForecast, ErrorList> Create(int year, int month, IEnumerable<Appointment> appointments, IEnumerable<Service> services, IEnumerable<AppointmentSettlement> settlements, DateTimeOffset now, IEnumerable<AppointmentId> plannedSubscriptionAppointments, IEnumerable<Payment>? payments = null)
    {
        if (year is < 1 or > 9999 || month is < 1 or > 12) return ErrorList.Single(new ModelError.InvalidValue(), "month");
        var catalogue = services.DistinctBy(item => item.Id).ToDictionary(item => item.Id);
        var charges = settlements.Where(item => !item.IsReversed).ToArray();
        var covered = plannedSubscriptionAppointments.ToHashSet();
        var paid = payments?.ToArray() ?? [];
        var lines = ImmutableArray.CreateBuilder<ForecastLine>();
        foreach (var appointment in appointments.Where(item => !item.IsDeleted && item.Status != AppointmentStatus.Cancelled).DistinctBy(item => item.Id))
        {
            var date = SchoolTime.Date(appointment.StartsAt);
            if (date.Year != year || date.Month != month) continue;
            if (appointment.Status == AppointmentStatus.Scheduled && appointment.EndsAt <= now) continue;

            var charge = charges.Where(item => item.AppointmentId == appointment.Id).ToArray();
            if (charge.Length > 1) return ErrorList.Single(new FinanceError.AlreadySettled());
            if (charge.FirstOrDefault()?.Source is SettlementSource.Cash cash)
                lines.Add(new(appointment.Id, cash.Amount, null));
            else if (charge.FirstOrDefault()?.Source is SettlementSource.Prepaid prepaid)
                lines.Add(new(appointment.Id, prepaid.Amount, null));
            else if (charge.Length == 0 && catalogue.TryGetValue(appointment.ServiceId, out var consultation) && consultation.IsConsultation)
                lines.Add(new(appointment.Id, Money.Create(0).Value, null));
            else if (charge.FirstOrDefault()?.Source is SettlementSource.Subscription || covered.Contains(appointment.Id))
                lines.Add(new(appointment.Id, null, ForecastUncertainty.SubscriptionValuation));
            else if (paid.Any(item => item.ClientId == appointment.ClientId && (item.Conditions is null
                || item.Conditions.ServiceId == appointment.ServiceId && item.RemainingLessons(charges) > 0)))
                lines.Add(new(appointment.Id, null, ForecastUncertainty.PaidReserveValuation));
            else if (!catalogue.TryGetValue(appointment.ServiceId, out var service) || service.PriceAt(appointment.StartsAt) is not { } price)
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
