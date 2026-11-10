using System.Collections.Immutable;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Domain;

public static class SettlementSelection
{
    public static Result<SettlementSource, ErrorList> Select(Appointment appointment, Service service,
        IEnumerable<Payment> payments, IEnumerable<AppointmentSettlement> history, DateTimeOffset recordedAt)
    {
        if (service.Id != appointment.ServiceId) return ErrorList.Single(new ModelError.InvalidValue(), "serviceId");
        if (service.IsConsultation) return new SettlementSource.Uncovered(Money.Create(0).Value, null);
        var snapshot = history.ToImmutableArray();
        var allPayments = payments.Where(item => item.ClientId == appointment.ClientId).ToArray();
        var validation = PaymentSnapshotRules.Validate(appointment.ClientId, appointment.ServiceId, allPayments, snapshot);
        if (validation.IsFailure) return validation.Error;
        var candidates = allPayments.Where(item => item.Conditions!.ServiceId == appointment.ServiceId && item.PaidAt <= recordedAt
            && item.RemainingLessons(snapshot) > 0).OrderBy(item => item.PaidAt).ThenBy(item => item.Id.Value).ToArray();
        foreach (var payment in candidates)
        {
            var recovered = payment.DebtCoverage.FirstOrDefault(coverage =>
                snapshot.Any(item => item.Id == coverage.SettlementId && item.IsReversed)
                && !snapshot.Any(item => !item.IsReversed && item.Source is SettlementSource.Prepaid source
                    && source.Id == payment.Id && source.RecoveredDebtId == coverage.SettlementId));
            if (recovered is not null) return new SettlementSource.Prepaid(payment.Id, recovered.Amount, recovered.SettlementId);
            // A later current-price payment must not reprice an older appointment (D-484).
            if (payment.PaidAt > appointment.StartsAt) continue;
            var usedCurrentUnits = snapshot.Count(item => !item.IsReversed && item.Source is SettlementSource.Prepaid source
                && source.Id == payment.Id && source.RecoveredDebtId is null);
            if (usedCurrentUnits >= payment.Conditions!.Quantity - payment.DebtCoverage.Length) continue;
            if (candidates.Any(other => other.Id != payment.Id && other.PaidAt == payment.PaidAt && other.PaidAt <= appointment.StartsAt))
                return ErrorList.Single(new FinanceError.AmbiguousCoverage());
            if (!payment.DebtCoverage.IsEmpty) return new SettlementSource.Prepaid(payment.Id, payment.Conditions.UnitPrice);
            var allocation = Money.Create(payment.Amount.Amount / payment.Conditions!.Quantity);
            if (allocation.IsFailure) return ErrorList.Single(new ModelError.DecisionRequired(), "partialPaymentAllocation");
            return new SettlementSource.Prepaid(payment.Id, allocation.Value);
        }
        var price = service.PriceAt(appointment.StartsAt);
        if (price is null) return ErrorList.Single(new FinanceError.MissingPrice());
        var amount = Money.Create(price.Value);
        if (amount.IsFailure) return amount.Error;
        return new SettlementSource.Uncovered(amount.Value, null);
    }
}
