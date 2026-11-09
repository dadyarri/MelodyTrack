using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Application;

public interface IMarketingCampaignStore
{
    Task<Result<MarketingCampaign?, ErrorList>> FindAsync(MarketingCampaignId id, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetCodesAsync(CancellationToken cancellationToken);
    Task<Result<MarketingCampaign, ErrorList>> InsertAsync(MarketingCampaign campaign, CancellationToken cancellationToken);
}

public sealed class MarketingCampaignService(IMarketingCampaignStore store)
{
    public async Task<Result<MarketingCampaign, ErrorList>> CreateAsync(MarketingCampaignId id, string? code,
        TimeInterval activationPeriod, Discount discount, Actor actor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validActor = actor.Validate();
        if (validActor.IsFailure) return validActor.Error;
        if (!actor.IsSuperuser()) return ErrorList.Single(new ModelError.Forbidden());
        var campaign = MarketingCampaignRules.Create(id, code, activationPeriod, discount, new DiscountTerms.Payment(), null);
        return campaign.IsFailure ? campaign.Error : await store.InsertAsync(campaign.Value, cancellationToken);
    }

    public async Task<Result<MarketingCampaign, ErrorList>> CreateWithGeneratedCodeAsync(MarketingCampaignId id,
        TimeInterval activationPeriod, Discount discount, string alphabet, Actor actor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validActor = actor.Validate();
        if (validActor.IsFailure) return validActor.Error;
        if (!actor.IsSuperuser()) return ErrorList.Single(new ModelError.Forbidden());
        if (id.Value == Guid.Empty) return ErrorList.Single(new IdentifierError.Empty());
        // Caller must supply the configured alphabet; D-412 has no approved product default.
        var generated = MarketingCodeRules.Generate([], alphabet);
        if (generated.IsFailure) return generated.Error;
        var existing = await store.FindAsync(id, cancellationToken);
        if (existing.IsFailure) return existing.Error;
        if (existing.Value is { } saved)
        {
            var candidate = MarketingCampaignRules.Create(id, saved.Code, activationPeriod, discount, new DiscountTerms.Payment(), null);
            return candidate.IsFailure ? candidate.Error : await store.InsertAsync(candidate.Value, cancellationToken);
        }
        var occupied = (await store.GetCodesAsync(cancellationToken)).Select(MarketingCodeRules.Normalize).ToHashSet(StringComparer.Ordinal);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            generated = MarketingCodeRules.Generate(occupied, alphabet);
            if (generated.IsFailure) return generated.Error;
            var candidate = MarketingCampaignRules.Create(id, generated.Value, activationPeriod, discount, new DiscountTerms.Payment(), null);
            if (candidate.IsFailure) return candidate.Error;
            var result = await store.InsertAsync(candidate.Value, cancellationToken);
            if (result.IsFailure && result.Error.Issues.Any(issue => issue.Error is ModelError.Duplicate && issue.Field == "id"))
            {
                var concurrent = await store.FindAsync(id, cancellationToken);
                if (concurrent.IsFailure) return concurrent.Error;
                if (concurrent.Value is not { } concurrentCampaign) return result;
                var replay = MarketingCampaignRules.Create(id, concurrentCampaign.Code, activationPeriod, discount, new DiscountTerms.Payment(), null);
                return replay.IsFailure ? replay.Error : await store.InsertAsync(replay.Value, cancellationToken);
            }
            if (result.IsSuccess || !result.Error.Issues.Any(issue => issue.Error is ModelError.Duplicate && issue.Field == "code"))
                return result;
            // Another writer claimed the candidate. Exclude it and advance until a free code or exhaustion.
            occupied.Add(MarketingCodeRules.Normalize(generated.Value));
        }
    }
}
