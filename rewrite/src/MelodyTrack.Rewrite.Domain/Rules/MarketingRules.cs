using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class MarketingCampaignRules
{
    public static Result<MarketingCampaign, ErrorList> Create(MarketingCampaignId id, string? code, TimeInterval activationPeriod, Discount discount, DiscountTerms terms, TimeSpan? returningClientGap)
    {
        if (terms is not DiscountTerms.Payment || returningClientGap is not null)
            return ErrorList.Single(new ModelError.InvalidValue(), "terms");
        var text = Validation.Required("code", code);
        if (text.IsFailure) return text.Error;
        return Restore(id, text.Value, activationPeriod, discount, terms, null);
    }

    public static Result<MarketingCampaign, ErrorList> Restore(MarketingCampaignId id, string? code, TimeInterval activationPeriod, Discount discount, DiscountTerms terms, TimeSpan? returningClientGap)
    {
        var text = Validation.Required("code", code);
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Check(text));
        if (validation.IsFailure) return validation.Error;
        if (returningClientGap is { } gap && gap <= TimeSpan.Zero) return ErrorList.Single(new ModelError.InvalidValue(), "returningClientGap");
        return new MarketingCampaign(id, code!, activationPeriod, discount, terms, returningClientGap);
    }

    public static Result<MarketingCampaign, ErrorList> Stop(this MarketingCampaign model) => model with { IsStopped = true };

    public static bool CanDelete(this MarketingCampaign model, IEnumerable<DiscountActivation> activations) => !activations.Any(item => item.CampaignId == model.Id);

    public static Result<DiscountActivation, ErrorList> Activate(this MarketingCampaign model, DiscountActivationId id, ClientId clientId, ServiceId serviceId, DateTimeOffset now, DateTimeOffset? lastOrdinaryLessonEndedAt, IEnumerable<DiscountActivation> previous)
    {
        if (model.IsStopped || !model.ActivationPeriod.Contains(now)) return ErrorList.Single(new FinanceError.CampaignUnavailable());
        if (previous.Any(item => item.CampaignId == model.Id && item.ClientId == clientId)) return ErrorList.Single(new ModelError.Duplicate());
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
        return new DiscountActivation(id, campaign, clientId, serviceId, activatedAt, null);
    }

    public static Result<DiscountActivation, ErrorList> Restore(DiscountActivationId id, MarketingCampaign campaign, ClientId clientId,
        ServiceId serviceId, DateTimeOffset activatedAt, DateTimeOffset? expiresAt)
    {
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Identifier(clientId.Value, "clientId"),
            Validation.Identifier(serviceId.Value, "serviceId"));
        return validation.IsFailure ? validation.Error : new DiscountActivation(id, campaign, clientId, serviceId, activatedAt, expiresAt);
    }
}
