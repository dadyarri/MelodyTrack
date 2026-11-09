using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class MarketingCampaignTests
{
    private static Actor Admin => new Actor.Staff(TestData.StaffId, StaffRole.Administrator);
    private static Actor Super => new Actor.Staff(TestData.StaffId, StaffRole.Superuser);
    private static MarketingCampaign Campaign() => Ok(MarketingCampaignOperations.Create(new(Guid(100)), "START", Period(Utc(1, 0), Utc(10, 0)),
        new Discount.Fixed(Ok(Money.Create(100))), new DiscountTerms.Payment(), null, Super, []));

    [Fact]
    public void Restore_CodeWithOriginalWhitespace_PreservesHistoricalRepresentation()
    {
        const string code = "  StArT  ";

        var restored = Ok(MarketingCampaignRules.Restore(new(Guid(100)), code, Period(Utc(1, 0), Utc(10, 0)),
            new Discount.Fixed(Ok(Money.Create(100))), new DiscountTerms.Payment(), null));

        Assert.Equal(code, restored.Code);
        Assert.Equal("  START  ", MarketingCodeRules.Normalize(restored.Code));
    }

    [Fact]
    public void Create_ManualCodeWithWhitespace_TrimsNewInput()
    {
        var created = Ok(MarketingCampaignRules.Create(new(Guid(100)), "  StArT  ", Period(Utc(1, 0), Utc(10, 0)),
            new Discount.Fixed(Ok(Money.Create(100))), new DiscountTerms.Payment(), null));

        Assert.Equal("StArT", created.Code);
    }

    [Fact]
    public void Stop_ExistingActivation_PreservesGrantedDiscount()
    {
        var campaign = Campaign();
        var activation = Ok(campaign.Activate(new(Guid(101)), TestData.ClientId, TestData.ServiceId, Utc(2, 0), null, [], Admin));

        var stopped = Ok(campaign.Stop(Super));
        var payment = Ok(PaymentRules.Create(new(Guid(102)), TestData.ClientId, MakeService(), 1, Utc(15, 15), null, activation, [], []));

        Assert.Equal(900m, payment.Amount.Amount);
        Assert.Null(activation.ExpiresAt);
        AssertError(new FinanceError.CampaignUnavailable(), stopped.Activate(new(Guid(103)), new(Guid(104)), TestData.ServiceId, Utc(3, 0), null, [], Admin));
    }

    [Fact]
    public void Activate_PreviousActivation_RejectsAnotherServiceForSameClient()
    {
        var campaign = Campaign();
        var activation = Ok(campaign.Activate(new(Guid(101)), TestData.ClientId, TestData.ServiceId, Utc(2, 0), null, [], Admin));

        AssertError(new ModelError.Duplicate(), campaign.Activate(new(Guid(102)), TestData.ClientId, new(Guid(103)), Utc(3, 0), null, [activation], Admin));
        Assert.False(campaign.CanDelete([activation]));
    }

    [Fact]
    public void Apply_FixedDiscountExceedsPrice_ReturnsZero()
    {
        var discount = new Discount.Fixed(Ok(Money.Create(2000)));

        Assert.Equal(0m, Ok(discount.Apply(Ok(Money.Create(1000)))).Amount);
    }

    [Fact]
    public void Activate_ExistingClientWithoutReturningGap_DoesNotApplySegmentation()
    {
        var activation = Ok(Campaign().Activate(new(Guid(101)), TestData.ClientId, TestData.ServiceId,
            Utc(3, 0), Utc(2, 0), [], Admin));

        Assert.Null(activation.ExpiresAt);
    }

    [Fact]
    public void Create_HistoricalTerms_RequiresExplicitHistoricalRestore()
    {
        var terms = Ok(DiscountTerms.Period.Create(TimeSpan.FromDays(30)));

        AssertError(new ModelError.InvalidValue(), MarketingCampaignOperations.Create(new(Guid(100)), "OLD",
            Period(Utc(1, 0), Utc(10, 0)), new Discount.Fixed(Ok(Money.Create(100))), terms, null, Super, []));
        var historical = Ok(MarketingCampaignRules.Restore(new(Guid(100)), "OLD", Period(Utc(1, 0), Utc(10, 0)),
            new Discount.Fixed(Ok(Money.Create(100))), terms, TimeSpan.FromDays(60)));
        var activation = Ok(DiscountActivationRules.Restore(new(Guid(101)), historical, TestData.ClientId,
            TestData.ServiceId, Utc(2, 0), Utc(31, 0)));

        Assert.Equal(terms, historical.Terms);
        Assert.Equal(Utc(31, 0), activation.ExpiresAt);
        Assert.Equal(TimeSpan.FromDays(60), historical.ReturningClientGap);
    }
}
