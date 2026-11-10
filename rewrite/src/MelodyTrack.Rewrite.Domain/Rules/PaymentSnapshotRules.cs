using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class PaymentSnapshotRules
{
    public static UnitResult<ErrorList> Validate(ClientId clientId, ServiceId serviceId,
        IEnumerable<Payment> payments, IEnumerable<AppointmentSettlement> history)
    {
        var paid = payments.ToArray();
        var settlements = history.ToArray();
        if (paid.Select(item => item.Id).Distinct().Count() != paid.Length
            || settlements.Select(item => item.Id).Distinct().Count() != settlements.Length)
            return Fail(new ModelError.Duplicate());
        var owned = paid.Where(item => item.ClientId == clientId).ToArray();
        if (owned.Any(item => item.Conditions is null)) return Fail(new ModelError.DecisionRequired(), "historicalPayment");
        var relevant = owned.Where(item => item.Conditions!.ServiceId == serviceId).ToArray();
        if (settlements.Any(item => item.Source is SettlementSource.Prepaid source
            && relevant.Any(payment => payment.Id == source.Id) && (item.ClientId != clientId || item.ServiceId != serviceId)))
            return Fail(new FinanceError.InconsistentSettlement(), "paymentId");
        var charges = settlements.Where(item => item.ClientId == clientId && item.ServiceId == serviceId).ToArray();
        var active = charges.Where(item => !item.IsReversed).ToArray();
        if (active.Select(item => item.AppointmentId).Distinct().Count() != active.Length)
            return Fail(new FinanceError.InconsistentSettlement());
        if (active.Any(item => item.Source is SettlementSource.Uncovered { DiscountId: not null }))
            return Fail(new ModelError.DecisionRequired(), "historicalSettlement");
        var byId = settlements.ToDictionary(item => item.Id);
        var covered = new HashSet<SettlementId>();
        foreach (var payment in relevant)
        {
            var terms = payment.Conditions!;
            if (payment.DebtCoverage.Length > terms.Quantity || !payment.DebtCoverage.IsEmpty && terms.DiscountId is not null)
                return Fail(new FinanceError.InconsistentSettlement());
            foreach (var coverage in payment.DebtCoverage)
            {
                if (!covered.Add(coverage.SettlementId) || !byId.TryGetValue(coverage.SettlementId, out var debt)
                    || debt.ClientId != clientId || debt.ServiceId != serviceId
                    || debt.Source is not SettlementSource.Uncovered { DiscountId: null } cash || cash.Amount.Amount <= 0
                    || cash.Amount != coverage.Amount)
                    return Fail(new FinanceError.InconsistentSettlement(), "debtCoverage");
            }
            var prepaid = charges.Where(item => item.Source is SettlementSource.Prepaid source && source.Id == payment.Id).ToArray();
            foreach (var settlement in prepaid)
            {
                var source = (SettlementSource.Prepaid)settlement.Source;
                if (source.RecoveredDebtId is { } debtId)
                {
                    var coverage = payment.DebtCoverage.FirstOrDefault(item => item.SettlementId == debtId);
                    if (coverage is null || source.Amount != coverage.Amount
                        || !settlement.IsReversed && !byId[debtId].IsReversed)
                        return Fail(new FinanceError.InconsistentSettlement(), "recoveredDebtId");
                }
                else
                {
                    var expected = payment.DebtCoverage.IsEmpty
                        ? payment.Amount.Amount / terms.Quantity : terms.UnitPrice.Amount;
                    if (source.Amount.Amount != expected)
                        return Fail(new FinanceError.InconsistentSettlement(), "amount");
                }
            }
            var current = prepaid.Where(item => !item.IsReversed).Select(item => (SettlementSource.Prepaid)item.Source).ToArray();
            if (current.Count(source => source.RecoveredDebtId is null) > terms.Quantity - payment.DebtCoverage.Length
                || current.Where(source => source.RecoveredDebtId is not null).GroupBy(source => source.RecoveredDebtId).Any(group => group.Count() > 1)
                || payment.RemainingLessons(settlements) < 0)
                return Fail(new FinanceError.InconsistentSettlement());
        }
        if (charges.Any(item => item.Source is SettlementSource.Prepaid source && !relevant.Any(payment => payment.Id == source.Id)))
            return Fail(new FinanceError.InconsistentSettlement(), "paymentId");
        return UnitResult.Success<ErrorList>();
    }

    private static UnitResult<ErrorList> Fail(DomainError error, string? field = null) =>
        UnitResult.Failure(ErrorList.Single(error, field));
}
