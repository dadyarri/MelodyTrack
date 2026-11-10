using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class PaymentRules
{
    public static Result<Payment, ErrorList> Create(PaymentId id, ClientId clientId, Service service, int quantity,
        DateTimeOffset paidAt, string? comment, DiscountActivation? discount, IEnumerable<Payment> previous,
        IEnumerable<AppointmentSettlement> history)
    {
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Identifier(clientId.Value, "clientId"));
        if (validation.IsFailure) return validation.Error;
        if (service.IsArchived || service.IsConsultation || quantity <= 0) return ErrorList.Single(new ModelError.InvalidValue(), "quantity");
        var payments = previous.ToArray();
        if (payments.Any(item => item.Id == id)) return ErrorList.Single(new ModelError.Duplicate(), "id");
        var settlements = history.ToArray();
        var snapshot = PaymentSnapshotRules.Validate(clientId, service.Id, payments, settlements);
        if (snapshot.IsFailure) return snapshot.Error;
        var coveredIds = payments.Where(payment => payment.ClientId == clientId && payment.Conditions?.ServiceId == service.Id)
            .SelectMany(payment => payment.DebtCoverage).Select(coverage => coverage.SettlementId).ToHashSet();
        var debts = settlements.Where(settlement => settlement.ClientId == clientId && settlement.ServiceId == service.Id
                && !settlement.IsReversed && !coveredIds.Contains(settlement.Id)
                && settlement.Source is SettlementSource.Uncovered { Amount.Amount: > 0, DiscountId: null })
            .OrderBy(settlement => settlement.StartsAt).ThenBy(settlement => settlement.Id.Value).ToArray();
        if (discount is not null && debts.Length > 0)
            return ErrorList.Single(new ModelError.DecisionRequired(), "discountedDebtAllocation");
        if (discount is not null && (discount.ClientId != clientId || discount.ServiceId != service.Id || paidAt < discount.ActivatedAt
            || payments.Any(item => item.Conditions?.DiscountId == discount.Id)))
            return ErrorList.Single(new FinanceError.DiscountUnavailable());
        if (service.PriceAt(paidAt) is not { } price) return ErrorList.Single(new FinanceError.MissingPrice());
        try
        {
            var coverage = debts.Take(quantity).Select(settlement =>
                new PaymentDebtCoverage(settlement.Id, ((SettlementSource.Uncovered)settlement.Source).Amount)).ToImmutableArray();
            var total = Money.Create(coverage.Sum(item => item.Amount.Amount) + price * (quantity - coverage.Length));
            if (total.IsFailure) return total.Error;
            var amount = discount is null ? total : discount.Discount.ApplyToPayment(total.Value);
            if (amount.IsFailure) return amount.Error;
            return new Payment(id, clientId, amount.Value, paidAt, Validation.OptionalText(comment),
                new(service.Id, quantity, Money.Create(price).Value, discount?.Id, discount?.Discount), coverage);
        }
        catch (OverflowException) { return ErrorList.Single(new ModelError.InvalidValue(), "quantity"); }
    }

    public static Result<Payment, ErrorList> Restore(PaymentId id, ClientId clientId, Money amount, DateTimeOffset paidAt, string? comment,
        PaymentConditions? conditions = null, IEnumerable<PaymentDebtCoverage>? debtCoverage = null)
    {
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Identifier(clientId.Value, "clientId"));
        if (validation.IsFailure) return validation.Error;
        if (conditions is { } terms)
        {
            if (terms.Quantity <= 0 || terms.ServiceId.Value == Guid.Empty || (terms.Discount is null) != (terms.DiscountId is null)
                || terms.DiscountId is { Value: var discountId } && discountId == Guid.Empty)
                return ErrorList.Single(new ModelError.InvalidValue(), "conditions");
        }
        var coverage = (debtCoverage ?? []).ToImmutableArray();
        if (!coverage.IsEmpty)
        {
            if (conditions is null || conditions.DiscountId is not null || coverage.Length > conditions.Quantity
                || coverage.Any(item => item.SettlementId.Value == Guid.Empty || item.Amount.Amount <= 0)
                || coverage.Select(item => item.SettlementId).Distinct().Count() != coverage.Length)
                return ErrorList.Single(new ModelError.InvalidValue(), "debtCoverage");
            try
            {
                if (coverage.Sum(item => item.Amount.Amount) + conditions.UnitPrice.Amount * (conditions.Quantity - coverage.Length) != amount.Amount)
                    return ErrorList.Single(new ModelError.InvalidValue(), "amount");
            }
            catch (OverflowException) { return ErrorList.Single(new ModelError.InvalidValue(), "amount"); }
        }
        return new Payment(id, clientId, amount, paidAt, comment, conditions, coverage);
    }

    public static int RemainingLessons(this Payment model, IEnumerable<AppointmentSettlement> history)
    {
        if (model.Conditions is not { } terms) return 0;
        var current = history.Where(item => !item.IsReversed).ToArray();
        return terms.Quantity - model.DebtCoverage.Count(coverage => current.Any(item => item.Id == coverage.SettlementId))
            - current.Where(item => item.Source is SettlementSource.Prepaid source && source.Id == model.Id)
                .Select(item => item.AppointmentId).Distinct().Count();
    }

    public static bool WasUsed(this Payment model, IEnumerable<AppointmentSettlement> history) =>
        !model.DebtCoverage.IsEmpty || history.Any(item => item.Source is SettlementSource.Prepaid source && source.Id == model.Id);
}

public static class RefundRules
{
    public static Result<Refund, ErrorList> Create(RefundId id, ClientId clientId, Money amount, DateTimeOffset refundedAt, string? comment)
    {
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Identifier(clientId.Value, "clientId"));
        if (validation.IsFailure) return validation.Error;
        if (amount.Amount == 0) return ErrorList.Single(new ModelError.InvalidValue(), "amount");
        return new Refund(id, clientId, amount, refundedAt, Validation.OptionalText(comment));
    }
}

public static class ExpenseCategoryRules
{
    public static Result<ExpenseCategory, ErrorList> Create(ExpenseCategoryId id, string? name)
    {
        var text = Validation.Required("name", name);
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Check(text));
        return validation.IsFailure ? validation.Error : new ExpenseCategory(id, text.Value);
    }
}

public static class ExpenseRules
{
    public static Result<Expense, ErrorList> Create(ExpenseId id, string? description, Money amount, DateOnly date, ExpenseCategoryId? categoryId)
    {
        var text = Validation.Required("description", description);
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Check(text),
            categoryId is { } category ? Validation.Identifier(category.Value, "categoryId") : UnitResult.Success<ErrorList>());
        if (validation.IsFailure) return validation.Error;
        return new Expense(id, text.Value, amount, date, categoryId);
    }
}

public static class AppointmentSettlementRules
{
    public static Result<AppointmentSettlement, ErrorList> Create(SettlementId id, Appointment appointment, SettlementSource source, DateTimeOffset recordedAt, IEnumerable<AppointmentSettlement> history)
    {
        var validation = Validation.Identifier(id.Value);
        if (validation.IsFailure) return validation.Error;
        if (appointment.IsDeleted || appointment.Status is not (AppointmentStatus.Completed or AppointmentStatus.Burned))
            return ErrorList.Single(new ModelError.InvalidState());
        if (history.Any(item => item.Id == id || item.AppointmentId == appointment.Id && !item.IsReversed))
            return ErrorList.Single(new FinanceError.AlreadySettled());
        var sourceValidation = source switch
        {
            SettlementSource.Prepaid payment => Validation.Collect(Validation.Identifier(payment.Id.Value, "paymentId"),
                payment.RecoveredDebtId is { } debt ? Validation.Identifier(debt.Value, "recoveredDebtId") : UnitResult.Success<ErrorList>()),
            SettlementSource.Uncovered { DiscountId: { } discount } => Validation.Identifier(discount.Value, "discountId"),
            SettlementSource.Uncovered => UnitResult.Success<ErrorList>()
        };
        if (sourceValidation.IsFailure) return sourceValidation.Error;
        return new AppointmentSettlement(id, appointment, source, recordedAt);
    }

    public static AppointmentSettlement Reverse(this AppointmentSettlement model) => model with { IsReversed = true };
}
