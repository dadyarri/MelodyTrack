using System.Collections.Immutable;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Domain;

public sealed record Payment
{
    internal Payment(PaymentId id, ClientId clientId, Money amount, DateTimeOffset paidAt, string? comment, PaymentConditions? conditions = null,
        ImmutableArray<PaymentDebtCoverage> debtCoverage = default)
    {
        Id = id; ClientId = clientId; Amount = amount; PaidAt = paidAt.ToUniversalTime(); Comment = comment; Conditions = conditions;
        DebtCoverage = debtCoverage.IsDefault ? [] : debtCoverage;
    }

    public PaymentId Id { get; }

    public ClientId ClientId { get; }

    public Money Amount { get; }

    public DateTimeOffset PaidAt { get; }

    public string? Comment { get; }

    public PaymentConditions? Conditions { get; }

    public ImmutableArray<PaymentDebtCoverage> DebtCoverage { get; }
}

public sealed record PaymentDebtCoverage(SettlementId SettlementId, Money Amount);
public sealed record PaymentConditions(ServiceId ServiceId, int Quantity, Money UnitPrice, DiscountActivationId? DiscountId, Discount? Discount);
public sealed record ServiceBalance(ServiceId ServiceId, decimal Amount, int RemainingLessons, int DiscountedLessons);

public sealed record Refund
{
    internal Refund(RefundId id, ClientId clientId, Money amount, DateTimeOffset refundedAt, string? comment)
    { Id = id; ClientId = clientId; Amount = amount; RefundedAt = refundedAt.ToUniversalTime(); Comment = comment; }

    public RefundId Id { get; }

    public ClientId ClientId { get; }

    public Money Amount { get; }

    public DateTimeOffset RefundedAt { get; }

    public string? Comment { get; }
}

public sealed record ExpenseCategory
{
    internal ExpenseCategory(ExpenseCategoryId id, string name) { Id = id; Name = name; }

    public ExpenseCategoryId Id { get; }

    public string Name { get; }
}

public sealed record Expense
{
    internal Expense(ExpenseId id, string description, Money amount, DateOnly date, ExpenseCategoryId? categoryId)
    { Id = id; Description = description; Amount = amount; Date = date; CategoryId = categoryId; }

    public ExpenseId Id { get; }

    public string Description { get; }

    public Money Amount { get; }

    public DateOnly Date { get; }

    public ExpenseCategoryId? CategoryId { get; }
}

public closed record SettlementSource
{
    public sealed record Prepaid(PaymentId Id, Money Amount, SettlementId? RecoveredDebtId = null) : SettlementSource;
    public sealed record Uncovered(Money Amount, DiscountActivationId? DiscountId) : SettlementSource;
}

/// <summary>One historical result charge. Reversal preserves its original source.</summary>
public sealed record AppointmentSettlement
{
    internal AppointmentSettlement(SettlementId id, Appointment appointment, SettlementSource source, DateTimeOffset recordedAt)
    {
        Id = id; AppointmentId = appointment.Id; ClientId = appointment.ClientId; ServiceId = appointment.ServiceId;
        StartsAt = appointment.StartsAt; EndsAt = appointment.Interval.EndsAt; Source = source; RecordedAt = recordedAt.ToUniversalTime();
    }

    public SettlementId Id { get; }

    public AppointmentId AppointmentId { get; }

    public ClientId ClientId { get; }

    public ServiceId ServiceId { get; }

    public DateTimeOffset StartsAt { get; }

    public DateTimeOffset EndsAt { get; }

    public SettlementSource Source { get; }

    public DateTimeOffset RecordedAt { get; }

    public bool IsReversed { get; internal init; }
}

public static class ClientBalance
{
    public static Result<ServiceBalance, ErrorList> CalculateForService(ClientId clientId, ServiceId serviceId,
        IEnumerable<Payment> payments, IEnumerable<AppointmentSettlement> settlements)
    {
        var paid = payments.Where(item => item.ClientId == clientId).ToArray();
        var history = settlements.ToArray();
        var validation = PaymentSnapshotRules.Validate(clientId, serviceId, paid, history);
        if (validation.IsFailure) return validation.Error;
        var charges = history.Where(item => item.ClientId == clientId && item.ServiceId == serviceId && !item.IsReversed).ToArray();
        if (paid.Any(item => item.Conditions is null) || charges.Any(item => item.Source is SettlementSource.Uncovered { DiscountId: not null }))
            return ErrorList.Single(new ModelError.DecisionRequired());
        var servicePayments = paid.Where(item => item.Conditions!.ServiceId == serviceId).ToArray();
        if (servicePayments.Any(item => item.RemainingLessons(charges) < 0)) return ErrorList.Single(new FinanceError.InconsistentSettlement());
        if (charges.Any(item => item.Source is SettlementSource.Prepaid source && !servicePayments.Any(payment => payment.Id == source.Id)))
            return ErrorList.Single(new FinanceError.InconsistentSettlement());
        var amount = servicePayments.Sum(item => item.Amount.Amount) - charges.Sum(item => item.Source switch
        {
            SettlementSource.Uncovered cash => cash.Amount.Amount,
            SettlementSource.Prepaid prepaid => prepaid.Amount.Amount
        });
        var remaining = servicePayments.Sum(item => item.RemainingLessons(history));
        var discounted = servicePayments.Where(item => item.Conditions!.DiscountId is not null).Sum(item => item.RemainingLessons(history));
        return new ServiceBalance(serviceId, amount, remaining, discounted);
    }
}
