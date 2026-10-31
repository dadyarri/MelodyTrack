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
        new Discount.Fixed(Ok(Money.Create(100))), Ok(DiscountTerms.Period.Create(TimeSpan.FromDays(30))), null, Super));

    [Fact]
    public void Stop_ExistingActivation_PreservesGrantedDiscount()
    {
        var campaign = Campaign();
        var activation = Ok(campaign.Activate(new(Guid(101)), TestData.ClientId, TestData.ServiceId, Utc(2, 0), null, [], Admin));

        var stopped = Ok(campaign.Stop(Super));
        var charge = Ok(activation.ApplyToLesson(MakeAppointment(102, Utc(15, 15)), Ok(Money.Create(1000)), []));

        Assert.Equal(900m, charge.Amount.Amount);
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
}
