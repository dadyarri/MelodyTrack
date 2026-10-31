using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Application.Operations;

public static class MarketingCampaignOperations
{
    public static Result<MarketingCampaign, ErrorList> Create(MarketingCampaignId id, string? code, TimeInterval activationPeriod, Discount discount, DiscountTerms terms, TimeSpan? returningClientGap, Actor actor)
    {
        if (!actor.IsSuperuser()) return ErrorList.Single(new ModelError.Forbidden());
        return MarketingCampaignRules.Create(id, code, activationPeriod, discount, terms, returningClientGap);
    }

    public static Result<MarketingCampaign, ErrorList> Stop(this MarketingCampaign model, Actor actor)
    {
        if (!actor.IsSuperuser()) return ErrorList.Single(new ModelError.Forbidden());
        return MarketingCampaignRules.Stop(model);
    }

    public static Result<DiscountActivation, ErrorList> Activate(this MarketingCampaign model, DiscountActivationId id, ClientId clientId, ServiceId serviceId, DateTimeOffset now, DateTimeOffset? lastOrdinaryLessonEndedAt, IEnumerable<DiscountActivation> previous, Actor actor)
    {
        if (!actor.IsReception()) return ErrorList.Single(new ModelError.Forbidden());
        return MarketingCampaignRules.Activate(model, id, clientId, serviceId, now, lastOrdinaryLessonEndedAt, previous);
    }
}
