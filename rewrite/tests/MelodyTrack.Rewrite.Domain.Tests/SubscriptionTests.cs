using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class SubscriptionTests
{
    [Fact]
    public void Restore_HistoricalExtensions_PreservesExactFacts()
    {
        var absences = new[] { Period(Utc(5, 15), Utc(8, 15)), Period(Utc(7, 15), Utc(10, 15)) };
        var baseExpiry = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var originalPrice = Ok(Money.Create(4000));

        var restored = Ok(SubscriptionRules.Restore(new(Guid(100)), TestData.ClientId, SubscriptionSize.Four,
            originalPrice, Utc(1, 15), baseExpiry, baseExpiry.AddDays(7), null, false, absences));

        Assert.Equal(baseExpiry, restored.BaseExpiresAt);
        Assert.Equal(baseExpiry.AddDays(7), restored.ExpiresAt);
        Assert.Equal(absences, restored.GrantedAbsences);
        Assert.Equal(originalPrice, restored.Price);
    }
}
