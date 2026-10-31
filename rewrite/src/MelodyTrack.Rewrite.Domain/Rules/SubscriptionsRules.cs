using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class SubscriptionRules
{
    public static Result<Subscription, ErrorList> Purchase(SubscriptionId id, ClientId clientId, SubscriptionSize size, Money price, DateTimeOffset purchasedAt, DiscountActivationId? discountId = null)
    {
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Identifier(clientId.Value, "clientId"),
            discountId is { } discount ? Validation.Identifier(discount.Value, "discountId") : UnitResult.Success<ErrorList>());
        if (validation.IsFailure) return validation.Error;
        if (!Enum.IsDefined(size)) return ErrorList.Single(new ModelError.InvalidValue(), "size");
        try
        {
            var local = SchoolTime.ToLocal(purchasedAt).DateTime.AddMonths(1);
            var expiry = SchoolTime.At(DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local));
            if (expiry.IsFailure) return expiry.Error;
            return new Subscription(id, clientId, size, price, purchasedAt.ToUniversalTime(), expiry.Value, discountId);
        }
        catch (ArgumentOutOfRangeException) { return ErrorList.Single(new TimeError.OutOfRange()); }
    }

    public static int Remaining(this Subscription model, IEnumerable<AppointmentSettlement> history) => (int)model.Size - history
        .Where(item => !item.IsReversed && item.Source is SettlementSource.Subscription source && source.Id == model.Id)
        .Select(item => item.AppointmentId).Distinct().Count();

    public static bool CanCover(this Subscription model, Appointment appointment, bool serviceIsEligible, IEnumerable<AppointmentSettlement> history) =>
        !model.IsCancelled && appointment.ClientId == model.ClientId && serviceIsEligible && model.PurchasedAt <= appointment.StartsAt
        && appointment.StartsAt < model.ExpiresAt && model.Remaining(history) > 0;

    public static Result<Subscription, ErrorList> Cancel(this Subscription model, IEnumerable<AppointmentSettlement> history)
    {
        if (history.Any(item => item.Source is SettlementSource.Subscription source && source.Id == model.Id))
            return ErrorList.Single(new FinanceError.SubscriptionUsed());
        return model with { IsCancelled = true };
    }

    public static Result<Subscription, ErrorList> Extend(this Subscription model, IEnumerable<TimeInterval> relevantAbsences)
    {
        if (model.IsCancelled) return ErrorList.Single(new ModelError.InvalidState());
        var union = new List<TimeInterval>();
        foreach (var interval in model.GrantedAbsences.Concat(relevantAbsences).Where(item => item.EndsAt > model.PurchasedAt).OrderBy(item => item.StartsAt))
        {
            var start = interval.StartsAt < model.PurchasedAt ? model.PurchasedAt : interval.StartsAt;
            if (union.Count > 0 && start <= union[^1].EndsAt)
            {
                var previous = union[^1];
                union[^1] = TimeInterval.Create(previous.StartsAt, previous.EndsAt > interval.EndsAt ? previous.EndsAt : interval.EndsAt).Value;
            }
            else union.Add(TimeInterval.Create(start, interval.EndsAt).Value);
        }
        var expiry = model.BaseExpiresAt;
        var granted = ImmutableArray.CreateBuilder<TimeInterval>();
        try
        {
            foreach (var interval in union)
            {
                if (interval.StartsAt >= expiry) break;
                expiry = expiry.Add(interval.Duration);
                granted.Add(interval);
            }
        }
        catch (ArgumentOutOfRangeException) { return ErrorList.Single(new TimeError.OutOfRange()); }
        return model with { ExpiresAt = expiry > model.ExpiresAt ? expiry : model.ExpiresAt, GrantedAbsences = granted.ToImmutable() };
    }
}
