using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain;

public sealed record Payment
{
    internal Payment(PaymentId id, ClientId clientId, Money amount, DateTimeOffset paidAt, string? comment)
    { Id = id; ClientId = clientId; Amount = amount; PaidAt = paidAt.ToUniversalTime(); Comment = comment; }

    public PaymentId Id { get; }

    public ClientId ClientId { get; }

    public Money Amount { get; }

    public DateTimeOffset PaidAt { get; }

    public string? Comment { get; }
}

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
    public sealed record Cash(Money Amount, DiscountActivationId? DiscountId) : SettlementSource;
    public sealed record Subscription(SubscriptionId Id) : SettlementSource;
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
    public static decimal Calculate(ClientId clientId, IEnumerable<Payment> payments, IEnumerable<Refund> refunds,
        IEnumerable<Subscription> subscriptions, IEnumerable<AppointmentSettlement> settlements) =>
        payments.Where(item => item.ClientId == clientId).DistinctBy(item => item.Id).Sum(item => item.Amount.Amount)
        - refunds.Where(item => item.ClientId == clientId).DistinctBy(item => item.Id).Sum(item => item.Amount.Amount)
        - subscriptions.Where(item => item.ClientId == clientId && !item.IsCancelled).DistinctBy(item => item.Id).Sum(item => item.Price.Amount)
        - settlements.Where(item => item.ClientId == clientId && !item.IsReversed).DistinctBy(item => item.Id)
            .Sum(item => item.Source is SettlementSource.Cash cash ? cash.Amount.Amount : 0);
}
