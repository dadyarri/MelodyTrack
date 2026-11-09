using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain;

public closed record Discount
{
    public sealed record Percentage : Discount
    {
        private Percentage(decimal value) => Value = value;
        public decimal Value { get; }
        public static Result<Percentage, ErrorList> Create(decimal value) => value <= 0 || value > 100
            ? ErrorList.Single(new ModelError.InvalidValue(), "percentage") : new Percentage(value);
    }
    public sealed record Fixed(Money Amount) : Discount;


}

public closed record DiscountTerms
{
    public sealed record Payment : DiscountTerms;
    public sealed record Lessons : DiscountTerms
    {
        private Lessons(int count) => Count = count;
        public int Count { get; }
        public static Result<Lessons, ErrorList> Create(int count) => count <= 0
            ? ErrorList.Single(new ModelError.InvalidValue(), "count") : new Lessons(count);
    }
    public sealed record Period : DiscountTerms
    {
        private Period(TimeSpan duration) => Duration = duration;
        public TimeSpan Duration { get; }
        public static Result<Period, ErrorList> Create(TimeSpan duration) => duration <= TimeSpan.Zero
            ? ErrorList.Single(new ModelError.InvalidValue(), "duration") : new Period(duration);
    }
    public sealed record Purchase : DiscountTerms
    {
        private Purchase(ImmutableHashSet<SubscriptionSize> sizes) => Sizes = sizes;
        public ImmutableHashSet<SubscriptionSize> Sizes { get; }
        public static Result<Purchase, ErrorList> Create(IEnumerable<SubscriptionSize> sizes)
        {
            var values = sizes.ToImmutableHashSet();
            return values.IsEmpty || values.Any(value => !Enum.IsDefined(value))
                ? ErrorList.Single(new ModelError.InvalidValue(), "sizes") : new Purchase(values);
        }
    }
}

public sealed record MarketingCampaign
{
    internal MarketingCampaign(MarketingCampaignId id, string code, TimeInterval activationPeriod, Discount discount,
        DiscountTerms terms, TimeSpan? returningClientGap)
    { Id = id; Code = code; ActivationPeriod = activationPeriod; Discount = discount; Terms = terms; ReturningClientGap = returningClientGap; }

    public MarketingCampaignId Id { get; }

    public string Code { get; }

    public TimeInterval ActivationPeriod { get; }

    public Discount Discount { get; }

    public DiscountTerms Terms { get; }

    public TimeSpan? ReturningClientGap { get; }

    public bool IsStopped { get; internal init; }
}

public sealed record DiscountActivation
{
    internal DiscountActivation(DiscountActivationId id, MarketingCampaign campaign, ClientId clientId, ServiceId serviceId,
        DateTimeOffset activatedAt, DateTimeOffset? expiresAt)
    {
        Id = id; CampaignId = campaign.Id; ClientId = clientId; ServiceId = serviceId; Discount = campaign.Discount;
        Terms = campaign.Terms; ActivatedAt = activatedAt.ToUniversalTime(); ExpiresAt = expiresAt;
    }

    public DiscountActivationId Id { get; }

    public MarketingCampaignId CampaignId { get; }

    public ClientId ClientId { get; }

    public ServiceId ServiceId { get; }

    public Discount Discount { get; }

    public DiscountTerms Terms { get; }

    public DateTimeOffset ActivatedAt { get; }

    public DateTimeOffset? ExpiresAt { get; }
}
