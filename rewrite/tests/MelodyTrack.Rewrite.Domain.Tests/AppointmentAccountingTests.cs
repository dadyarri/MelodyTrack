using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class AppointmentAccountingTests
{
    private static Actor Super => new Actor.Staff(TestData.StaffId, StaffRole.Superuser);
    private static DiscountActivation Discount(int campaignNumber = 200)
    {
        var campaign = Ok(MarketingCampaignOperations.Create(new(Guid(campaignNumber)), "START", Period(Utc(1, 0), Utc(20, 0)),
            new global::MelodyTrack.Rewrite.Domain.Discount.Fixed(Ok(Money.Create(100))), Ok(DiscountTerms.Lessons.Create(1)), null, Super));
        return Ok(campaign.Activate(new(Guid(campaignNumber + 1)), TestData.ClientId, TestData.ServiceId, Utc(2, 0), null, [], Super));
    }
    private static Subscription Purchase(int number = 300) => Ok(SubscriptionRules.Purchase(new(Guid(number)), TestData.ClientId,
        SubscriptionSize.Four, Ok(Money.Create(4000)), Utc(1, 0)));

    [Fact]
    public void SetResult_CoveredLesson_ConsumesSubscriptionWithoutDiscountOrCashCharge()
    {
        var appointment = MakeAppointment(100, Utc(5, 15));
        var subscription = Purchase();
        var discount = Discount();

        var change = Ok(AppointmentAccounting.SetResult(appointment, AppointmentStatus.Completed, new(Guid(101)),
            MakeService(), [subscription], [discount], [], Super, Utc(5, 17)));

        Assert.Equal(AppointmentStatus.Completed, change.Appointment.Status);
        var settlement = Assert.Single(change.SettlementChanges);
        Assert.Equal(new SettlementSource.Subscription(subscription.Id), settlement.Source);
        Assert.Equal(3, subscription.Remaining([settlement]));
        Assert.Equal(0m, ClientBalance.Calculate(TestData.ClientId, [], [], [], [settlement]));
        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
    }

    [Fact]
    public void SetResult_CashLesson_UsesPriceAtEndAndAppliesDiscount()
    {
        var appointment = MakeAppointment(100, Utc(5, 15));
        var service = Ok(MakeService().SchedulePrice(Now, Ok(ServicePrice.Create(Utc(5, 16), 1500))));

        var change = Ok(AppointmentAccounting.SetResult(appointment, AppointmentStatus.Completed, new(Guid(101)),
            service, [], [Discount()], [], Super, Utc(10, 0)));

        var cash = Assert.IsType<SettlementSource.Cash>(Assert.Single(change.SettlementChanges).Source);
        Assert.Equal(1400m, cash.Amount.Amount);
        Assert.NotNull(cash.DiscountId);
    }

    [Fact]
    public void SetResult_ExhaustedLessonDiscount_UsesFullPrice()
    {
        var discount = Discount();
        var first = Ok(AppointmentAccounting.SetResult(MakeAppointment(100, Utc(5, 15)), AppointmentStatus.Completed, new(Guid(101)),
            MakeService(), [], [discount], [], Super, Utc(5, 17)));

        var second = Ok(AppointmentAccounting.SetResult(MakeAppointment(102, Utc(6, 15)), AppointmentStatus.Burned, new(Guid(103)),
            MakeService(), [], [discount], first.SettlementChanges, Super, Utc(6, 17)));

        var cash = Assert.IsType<SettlementSource.Cash>(Assert.Single(second.SettlementChanges).Source);
        Assert.Equal(1000m, cash.Amount.Amount);
        Assert.Null(cash.DiscountId);
    }

    [Fact]
    public void SetResult_RepeatedResult_PreservesRecordedSourceWithoutAnotherSettlement()
    {
        var first = Ok(AppointmentAccounting.SetResult(MakeAppointment(100, Utc(5, 15)), AppointmentStatus.Completed, new(Guid(101)),
            MakeService(), [], [Discount()], [], Super, Utc(5, 17)));

        var repeated = Ok(AppointmentAccounting.SetResult(first.Appointment, AppointmentStatus.Completed, new(Guid(102)),
            MakeService(2000), [Purchase()], [], first.SettlementChanges, Super, Utc(6, 17)));

        Assert.Equal(first.Appointment, repeated.Appointment);
        Assert.Empty(repeated.SettlementChanges);
    }

    [Fact]
    public void ClearResult_OneOfTwoCoveredLessons_ReversesOnlySelectedLesson()
    {
        var subscription = Purchase();
        var first = Ok(AppointmentAccounting.SetResult(MakeAppointment(100, Utc(5, 15)), AppointmentStatus.Completed, new(Guid(101)),
            MakeService(), [subscription], [], [], Super, Utc(5, 17)));
        var second = Ok(AppointmentAccounting.SetResult(MakeAppointment(102, Utc(6, 15)), AppointmentStatus.Burned, new(Guid(103)),
            MakeService(), [subscription], [], first.SettlementChanges, Super, Utc(6, 17)));
        var history = first.SettlementChanges.AddRange(second.SettlementChanges);

        var cleared = Ok(AppointmentAccounting.ClearResult(first.Appointment, history, Super, Utc(7, 0)));

        Assert.Equal(AppointmentStatus.Scheduled, cleared.Appointment.Status);
        var reversed = Assert.Single(cleared.SettlementChanges);
        Assert.True(reversed.IsReversed);
        Assert.Equal(first.Appointment.Id, reversed.AppointmentId);
        Assert.Equal(first.SettlementChanges[0].Source, reversed.Source);
        Assert.Equal(3, subscription.Remaining([reversed, second.SettlementChanges[0]]));
        Assert.False(second.SettlementChanges[0].IsReversed);
        AssertError(new FinanceError.SubscriptionUsed(), subscription.Cancel([reversed, second.SettlementChanges[0]]));
    }

    [Fact]
    public void SetResult_AfterReversal_SelectsSourceAgainWithoutChangingOtherLessons()
    {
        var original = Ok(AppointmentAccounting.SetResult(MakeAppointment(100, Utc(5, 15)), AppointmentStatus.Completed, new(Guid(101)),
            MakeService(), [], [], [], Super, Utc(5, 17)));
        var cleared = Ok(AppointmentAccounting.ClearResult(original.Appointment, original.SettlementChanges, Super, Utc(6, 0)));
        var subscription = Purchase();

        var reapplied = Ok(AppointmentAccounting.SetResult(cleared.Appointment, AppointmentStatus.Burned, new(Guid(102)),
            MakeService(), [subscription], [], cleared.SettlementChanges, Super, Utc(6, 0)));

        Assert.Equal(new SettlementSource.Subscription(subscription.Id), Assert.Single(reapplied.SettlementChanges).Source);
        Assert.True(cleared.SettlementChanges[0].IsReversed);
        Assert.Equal(3, subscription.Remaining(cleared.SettlementChanges.AddRange(reapplied.SettlementChanges)));
    }

    [Fact]
    public void ClearResult_ResultWithoutSettlement_RejectsInconsistentSnapshot()
    {
        var completed = Ok(MakeAppointment(100, Utc(5, 15)).Complete(StaffRole.Superuser, Utc(5, 17)));

        AssertError(new FinanceError.InconsistentSettlement(), AppointmentAccounting.ClearResult(completed, [], Super, Utc(5, 17)));
    }

    [Fact]
    public void SetResult_ScheduledAppointmentWithActiveCharge_RejectsInconsistentSnapshot()
    {
        var appointment = MakeAppointment(100, Utc(5, 15));
        var first = Ok(AppointmentAccounting.SetResult(appointment, AppointmentStatus.Completed, new(Guid(101)),
            MakeService(), [], [], [], Super, Utc(5, 17)));

        AssertError(new FinanceError.InconsistentSettlement(), AppointmentAccounting.SetResult(appointment, AppointmentStatus.Completed,
            new(Guid(102)), MakeService(), [], [], first.SettlementChanges, Super, Utc(5, 17)));
    }

    [Fact]
    public void SetResult_EquallyExpiringSubscriptions_RejectsWithoutChangingAppointment()
    {
        var appointment = MakeAppointment(100, Utc(5, 15));

        AssertError(new FinanceError.AmbiguousCoverage(), AppointmentAccounting.SetResult(appointment, AppointmentStatus.Completed,
            new(Guid(101)), MakeService(), [Purchase(300), Purchase(301)], [], [], Super, Utc(5, 17)));
        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
    }

    [Fact]
    public void SetResult_MultipleApplicableDiscounts_RequiresExplicitResolution()
    {
        AssertError(new FinanceError.AmbiguousDiscount(), AppointmentAccounting.SetResult(MakeAppointment(100, Utc(5, 15)),
            AppointmentStatus.Completed, new(Guid(101)), MakeService(), [], [Discount(200), Discount(202)], [], Super, Utc(5, 17)));
    }

    [Fact]
    public void SetResult_Teacher_RejectsResultAndFinancialChange()
    {
        var teacher = new Actor.Staff(TestData.StaffId, StaffRole.Teacher);

        AssertError(new AppointmentError.Forbidden(), AppointmentAccounting.SetResult(MakeAppointment(100, Utc(5, 15)),
            AppointmentStatus.Completed, new(Guid(101)), MakeService(), [], [], [], teacher, Utc(5, 17)));
    }

    [Fact]
    public void SetResult_DiscountNeedsRounding_RejectsInsteadOfChargingFullPrice()
    {
        var campaign = Ok(MarketingCampaignOperations.Create(new(Guid(200)), "SMALL", Period(Utc(1, 0), Utc(20, 0)),
            Ok(global::MelodyTrack.Rewrite.Domain.Discount.Percentage.Create(12.5m)), Ok(DiscountTerms.Lessons.Create(1)), null, Super));
        var activation = Ok(campaign.Activate(new(Guid(201)), TestData.ClientId, TestData.ServiceId, Utc(2, 0), null, [], Super));
        var appointment = MakeAppointment(100, Utc(5, 15));

        AssertError(new ModelError.DecisionRequired(), AppointmentAccounting.SetResult(appointment, AppointmentStatus.Completed,
            new(Guid(101)), MakeService(0.01m), [], [activation], [], Super, Utc(5, 17)));
        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
    }

    [Fact]
    public void SetResult_AppointmentHasNotEnded_RejectsBeforeCreatingSettlement()
    {
        AssertError(new AppointmentError.ResultInFuture(), AppointmentAccounting.SetResult(MakeAppointment(100, Utc(5, 15)),
            AppointmentStatus.Completed, new(Guid(101)), MakeService(), [], [], [], Super, Utc(5, 15)));
    }
}
