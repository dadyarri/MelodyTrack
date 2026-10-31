using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class MarketingCampaignRules
{
    public static Result<MarketingCampaign, ErrorList> Create(MarketingCampaignId id, string? code, TimeInterval activationPeriod, Discount discount, DiscountTerms terms, TimeSpan? returningClientGap)
    {
        var text = Validation.Required("code", code);
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Check(text));
        if (validation.IsFailure) return validation.Error;
        if (returningClientGap is { } gap && gap <= TimeSpan.Zero) return ErrorList.Single(new ModelError.InvalidValue(), "returningClientGap");
        return new MarketingCampaign(id, text.Value, activationPeriod, discount, terms, returningClientGap);
    }

    public static Result<MarketingCampaign, ErrorList> Stop(this MarketingCampaign model) => model with { IsStopped = true };

    public static bool CanDelete(this MarketingCampaign model, IEnumerable<DiscountActivation> activations) => !activations.Any(item => item.CampaignId == model.Id);

    public static Result<DiscountActivation, ErrorList> Activate(this MarketingCampaign model, DiscountActivationId id, ClientId clientId, ServiceId serviceId, DateTimeOffset now, DateTimeOffset? lastOrdinaryLessonEndedAt, IEnumerable<DiscountActivation> previous)
    {
        if (model.IsStopped || !model.ActivationPeriod.Contains(now)) return ErrorList.Single(new FinanceError.CampaignUnavailable());
        if (previous.Any(item => item.CampaignId == model.Id && item.ClientId == clientId)) return ErrorList.Single(new ModelError.Duplicate());
        if (lastOrdinaryLessonEndedAt is { } last && (model.ReturningClientGap is not { } gap || now < last || now - last < gap))
            return ErrorList.Single(new FinanceError.DiscountUnavailable());
        return DiscountActivationRules.Create(id, model, clientId, serviceId, now);
    }
}

public static class DiscountActivationRules
{
    internal static Result<DiscountActivation, ErrorList> Create(DiscountActivationId id, MarketingCampaign campaign, ClientId clientId, ServiceId serviceId, DateTimeOffset activatedAt)
    {
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Identifier(clientId.Value, "clientId"),
            Validation.Identifier(serviceId.Value, "serviceId"));
        if (validation.IsFailure) return validation.Error;
        try
        {
            DateTimeOffset? expires = campaign.Terms is DiscountTerms.Period period ? activatedAt.Add(period.Duration).ToUniversalTime() : null;
            return new DiscountActivation(id, campaign, clientId, serviceId, activatedAt, expires);
        }
        catch (ArgumentOutOfRangeException) { return ErrorList.Single(new TimeError.OutOfRange()); }
    }

    public static Result<SettlementSource.Cash, ErrorList> ApplyToLesson(this DiscountActivation model, Appointment appointment, Money price, IEnumerable<AppointmentSettlement> history)
    {
        if (appointment.ClientId != model.ClientId || appointment.ServiceId != model.ServiceId || appointment.EndsAt < model.ActivatedAt)
            return ErrorList.Single(new FinanceError.DiscountUnavailable());
        if (model.Terms is DiscountTerms.Purchase) return ErrorList.Single(new FinanceError.DiscountUnavailable());
        if (model.Terms is DiscountTerms.Lessons lessons && history.Where(item => !item.IsReversed &&
            item.Source is SettlementSource.Cash { DiscountId: { } discount } && discount == model.Id)
            .Select(item => item.AppointmentId).Distinct().Count() >= lessons.Count)
            return ErrorList.Single(new FinanceError.DiscountUnavailable());
        if (model.ExpiresAt is { } expiry)
        {
            if (appointment.EndsAt == expiry) return ErrorList.Single(new ModelError.DecisionRequired(), "discountBoundary");
            if (appointment.EndsAt > expiry) return ErrorList.Single(new FinanceError.DiscountUnavailable());
        }
        var discounted = model.Discount.Apply(price);
        if (discounted.IsFailure) return discounted.Error;
        return new SettlementSource.Cash(discounted.Value, model.Id);
    }

    public static Result<Money, ErrorList> ApplyToPurchase(this DiscountActivation model, ClientId clientId, ServiceId serviceId, SubscriptionSize size, DateTimeOffset purchasedAt, Money price, IEnumerable<Subscription> purchases)
    {
        if (clientId != model.ClientId || serviceId != model.ServiceId || purchasedAt < model.ActivatedAt ||
            model.Terms is not DiscountTerms.Purchase terms || !terms.Sizes.Contains(size) ||
            purchases.Any(item => item.DiscountId == model.Id && !item.IsCancelled))
            return ErrorList.Single(new FinanceError.DiscountUnavailable());
        return model.Discount.Apply(price);
    }
}
