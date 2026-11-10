using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class MonthlyForecastRules
{
    /// <summary>Проецирует переданный снимок до конца месяца без записи списаний.</summary>
    /// <remarks>Передаются все ещё не прошедшие занятия до конца целевого месяца, включая предыдущие месяцы,
    /// а также полный снимок платежей и истории списаний соответствующих клиентов и услуг.</remarks>
    public static Result<MonthlyForecast, ErrorList> Create(int year, int month, IEnumerable<Appointment> appointments,
        IEnumerable<Service> services, IEnumerable<AppointmentSettlement> settlements, DateTimeOffset now,
        IEnumerable<Payment>? payments = null)
    {
        if (year is < 1 or > 9999 || month is < 1 or > 12) return ErrorList.Single(new ModelError.InvalidValue(), "month");
        var catalogue = services.DistinctBy(item => item.Id).ToDictionary(item => item.Id);
        var history = settlements.ToArray();
        var charges = history.Where(item => !item.IsReversed).ToLookup(item => item.AppointmentId);
        var paid = payments?.Where(item => item.PaidAt <= now).ToArray() ?? [];
        var reserves = new Dictionary<(ClientId, ServiceId), PaymentReserve[]>();
        var lines = ImmutableArray.CreateBuilder<ForecastLine>();
        foreach (var appointment in appointments.Where(item => !item.IsDeleted && item.Status != AppointmentStatus.Cancelled)
                     .DistinctBy(item => item.Id).OrderBy(item => item.StartsAt).ThenBy(item => item.Id.Value))
        {
            var date = SchoolTime.Date(appointment.StartsAt);
            if (date.Year > year || date.Year == year && date.Month > month) break;
            if (appointment.Status == AppointmentStatus.Scheduled && appointment.EndsAt <= now) continue;

            var inMonth = date.Year == year && date.Month == month;
            if (!inMonth && appointment.Status != AppointmentStatus.Scheduled) continue;

            var line = ProjectLine(appointment);
            if (line.IsFailure) return line.Error;
            if (inMonth) lines.Add(line.Value);
        }
        return new MonthlyForecast(year, month, lines.ToImmutable());

        Result<ForecastLine, ErrorList> ProjectLine(Appointment appointment)
        {
            var charge = charges[appointment.Id].ToArray();
            if (charge.Length > 1) return ErrorList.Single(new FinanceError.AlreadySettled());
            var source = charge.FirstOrDefault()?.Source;
            if (source is SettlementSource.Uncovered cash) return new ForecastLine.Known(appointment.Id, cash.Amount);
            if (source is SettlementSource.Prepaid prepaid) return new ForecastLine.Known(appointment.Id, prepaid.Amount);

            catalogue.TryGetValue(appointment.ServiceId, out var service);
            if (charge.Length == 0 && service is { IsConsultation: true })
                return new ForecastLine.Known(appointment.Id, Money.Create(0).Value);

            var key = (appointment.ClientId, appointment.ServiceId);
            if (!reserves.TryGetValue(key, out var available))
            {
                var validation = PaymentSnapshotRules.Validate(appointment.ClientId, appointment.ServiceId, paid, history);
                if (validation.IsFailure)
                {
                    if (!validation.Error.Issues.All(issue => issue.Error is ModelError.DecisionRequired)) return validation.Error;
                    return new ForecastLine.Unresolved(appointment.Id, ForecastUncertainty.PaidReserveValuation);
                }
                available = paid.Where(payment => payment.ClientId == appointment.ClientId && payment.Conditions!.ServiceId == appointment.ServiceId)
                    .OrderBy(payment => payment.PaidAt).ThenBy(payment => payment.Id.Value)
                    .Select(payment => new PaymentReserve(payment, history)).ToArray();
                reserves.Add(key, available);
            }

            var candidates = available.Where(reserve => reserve.HasRecovered
                || reserve.HasRemaining && reserve.Payment.PaidAt <= appointment.StartsAt).ToArray();
            if (candidates.FirstOrDefault() is { } first)
            {
                if (!first.HasRecovered && candidates.Skip(1).Any(other => other.Payment.PaidAt == first.Payment.PaidAt))
                    return new ForecastLine.Unresolved(appointment.Id, ForecastUncertainty.PaidReserveValuation);
                var allocation = first.Take();
                return allocation.IsSuccess ? new ForecastLine.Known(appointment.Id, allocation.Value)
                    : new ForecastLine.Unresolved(appointment.Id, ForecastUncertainty.PaidReserveValuation);
            }

            if (service?.PriceAt(appointment.StartsAt) is not { } price)
                return new ForecastLine.Unresolved(appointment.Id, ForecastUncertainty.MissingPrice);
            var amount = Money.Create(price);
            return amount.IsSuccess ? new ForecastLine.Known(appointment.Id, amount.Value) : amount.Error;
        }
    }

    public static decimal KnownValue(this MonthlyForecast model) => model.Lines.Sum(line => line switch
    {
        ForecastLine.Known known => known.Value.Amount,
        ForecastLine.Unresolved => 0m
    });

    public static bool IsComplete(this MonthlyForecast model) => model.Lines.All(line => line is ForecastLine.Known);

    private sealed class PaymentReserve
    {
        private readonly Queue<Money> recovered;
        private int remaining;
        public Payment Payment { get; }
        public bool HasRecovered => recovered.Count > 0;
        public bool HasRemaining => recovered.Count > 0 || remaining > 0;

        public PaymentReserve(Payment payment, AppointmentSettlement[] history)
        {
            Payment = payment;
            var used = history.Where(item => !item.IsReversed).Select(item => item.Source)
                .OfType<SettlementSource.Prepaid>().Where(source => source.Id == payment.Id).ToArray();
            var reversed = history.Where(item => item.IsReversed).Select(item => item.Id).ToHashSet();
            recovered = new Queue<Money>(payment.DebtCoverage.Where(coverage =>
                reversed.Contains(coverage.SettlementId) && !used.Any(source => source.RecoveredDebtId == coverage.SettlementId))
                .Select(coverage => coverage.Amount));
            remaining = payment.Conditions!.Quantity - payment.DebtCoverage.Length
                - used.Count(source => source.RecoveredDebtId is null);
        }

        public Result<Money, ErrorList> Take()
        {
            if (recovered.Count > 0) return recovered.Dequeue();
            remaining--;
            return Payment.DebtCoverage.IsEmpty ? Money.Create(Payment.Amount.Amount / Payment.Conditions!.Quantity)
                : Payment.Conditions!.UnitPrice;
        }
    }
}
