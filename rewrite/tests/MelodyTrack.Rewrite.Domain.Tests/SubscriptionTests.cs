using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class SubscriptionTests
{
    private static Subscription Purchase() => Ok(SubscriptionRules.Purchase(new(Guid(100)), TestData.ClientId,
        SubscriptionSize.Four, Ok(Money.Create(4000)), Utc(1, 15)));

    [Fact]
    public void Purchase_LastDayOfMonth_ClampsExpiryInSchoolTime()
    {
        var bought = Ok(SchoolTime.At(new(2026, 1, 31), new(15, 30)));

        var subscription = Ok(SubscriptionRules.Purchase(new(Guid(100)), TestData.ClientId, SubscriptionSize.Four, Ok(Money.Create(4000)), bought));

        Assert.Equal(Ok(SchoolTime.At(new(2026, 2, 28), new(15, 30))), subscription.ExpiresAt);
    }

    [Fact]
    public void Extend_OverlappingAbsences_GrantsUnionOnceAndPreservesItAfterRemoval()
    {
        var subscription = Purchase();
        var periods = new[] { Period(Utc(5, 15), Utc(8, 15)), Period(Utc(7, 15), Utc(10, 15)) };

        var extended = Ok(subscription.Extend(periods));
        var repeated = Ok(extended.Extend(periods));
        var removed = Ok(repeated.Extend([]));

        Assert.Equal(subscription.BaseExpiresAt.AddDays(5), extended.ExpiresAt);
        Assert.Equal(extended.ExpiresAt, repeated.ExpiresAt);
        Assert.Equal(extended.ExpiresAt, removed.ExpiresAt);
    }

    [Fact]
    public void Extend_AbsenceInGrantedPeriod_ExtendsThroughWholeAbsence()
    {
        var subscription = Purchase();
        var first = Period(subscription.BaseExpiresAt.AddDays(-1), subscription.BaseExpiresAt.AddDays(2));
        var second = Period(subscription.BaseExpiresAt.AddDays(2), subscription.BaseExpiresAt.AddDays(4));

        var extended = Ok(subscription.Extend([second, first]));

        Assert.Equal(subscription.BaseExpiresAt.AddDays(5), extended.ExpiresAt);
    }

    [Fact]
    public void Cancel_ReversedHistoricalUse_StillRejectsCancellation()
    {
        var subscription = Purchase();
        var appointment = Ok(MakeAppointment(101, Utc(5, 15)).Complete(StaffRole.Superuser, Utc(5, 17)));
        var charge = Ok(AppointmentSettlementRules.Create(new(Guid(102)), appointment, new SettlementSource.Subscription(subscription.Id), Utc(5, 17), []));

        var history = new[] { charge.Reverse() };

        Assert.Equal(4, subscription.Remaining(history));
        AssertError(new FinanceError.SubscriptionUsed(), subscription.Cancel(history));
    }
}
