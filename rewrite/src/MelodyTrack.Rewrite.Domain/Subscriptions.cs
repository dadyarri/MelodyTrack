using System.Collections.Immutable;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Domain;

public enum SubscriptionSize { Four = 4, Eight = 8 }

public sealed record Subscription
{
    internal Subscription(SubscriptionId id, ClientId clientId, SubscriptionSize size, Money price,
        DateTimeOffset purchasedAt, DateTimeOffset expiresAt, DiscountActivationId? discountId)
    {
        Id = id; ClientId = clientId; Size = size; Price = price; PurchasedAt = purchasedAt;
        BaseExpiresAt = expiresAt; ExpiresAt = expiresAt; DiscountId = discountId;
    }

    public SubscriptionId Id { get; }

    public ClientId ClientId { get; }

    public SubscriptionSize Size { get; }

    public Money Price { get; }

    public DateTimeOffset PurchasedAt { get; }

    public DateTimeOffset BaseExpiresAt { get; }

    public DateTimeOffset ExpiresAt { get; internal init; }

    public DiscountActivationId? DiscountId { get; }

    public bool IsCancelled { get; internal init; }

    public ImmutableArray<TimeInterval> GrantedAbsences { get; internal init; } = [];
}

public static class SettlementSelection
{
    public static Result<SettlementSource, ErrorList> Select(Appointment appointment, Service service,
        IEnumerable<Subscription> eligibleSubscriptions, IEnumerable<AppointmentSettlement> history)
    {
        if (service.Id != appointment.ServiceId) return ErrorList.Single(new ModelError.InvalidValue(), "serviceId");
        var snapshot = history.ToImmutableArray();
        var subscriptions = eligibleSubscriptions.DistinctBy(item => item.Id)
            .Where(item => item.CanCover(appointment, true, snapshot)).OrderBy(item => item.ExpiresAt).ToArray();
        if (subscriptions.Length > 1 && subscriptions[0].ExpiresAt == subscriptions[1].ExpiresAt)
            return ErrorList.Single(new FinanceError.AmbiguousCoverage());
        if (subscriptions.Length > 0) return new SettlementSource.Subscription(subscriptions[0].Id);
        var price = service.PriceAt(appointment.EndsAt);
        if (price is null) return ErrorList.Single(new FinanceError.MissingPrice());
        var amount = Money.Create(price.Value);
        if (amount.IsFailure) return amount.Error;
        return new SettlementSource.Cash(amount.Value, null);
    }
}
