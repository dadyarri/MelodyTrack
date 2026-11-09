using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class SubscriptionRules
{
    // Historical facts only. Current purchases are service-specific Payments (D-428/D-481).
    public static Result<Subscription, ErrorList> Restore(SubscriptionId id, ClientId clientId, SubscriptionSize size, Money price,
        DateTimeOffset purchasedAt, DateTimeOffset baseExpiresAt, DateTimeOffset expiresAt, DiscountActivationId? discountId,
        bool isCancelled, IEnumerable<TimeInterval> grantedAbsences)
    {
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Identifier(clientId.Value, "clientId"),
            discountId is { } discount ? Validation.Identifier(discount.Value, "discountId") : UnitResult.Success<ErrorList>());
        if (validation.IsFailure) return validation.Error;
        if (!Enum.IsDefined(size)) return ErrorList.Single(new ModelError.InvalidValue(), "size");
        return new Subscription(id, clientId, size, price, purchasedAt.ToUniversalTime(), baseExpiresAt.ToUniversalTime(), discountId)
        {
            ExpiresAt = expiresAt.ToUniversalTime(), IsCancelled = isCancelled, GrantedAbsences = grantedAbsences.ToImmutableArray()
        };
    }

    public static int Remaining(this Subscription model, IEnumerable<AppointmentSettlement> history) => (int)model.Size - history
        .Where(item => !item.IsReversed && item.Source is SettlementSource.Subscription source && source.Id == model.Id)
        .Select(item => item.AppointmentId).Distinct().Count();
}
