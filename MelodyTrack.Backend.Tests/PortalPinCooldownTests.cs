using MelodyTrack.Backend.Api.ClientPortal;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public sealed class PortalPinCooldownTests
{
    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 30)]
    [InlineData(4, 60)]
    [InlineData(5, 300)]
    [InlineData(6, 900)]
    [InlineData(7, 3600)]
    public void GetBlockedUntilUtc_FailedAttempts_AppliesEscalatingDelay(int attempts, int expectedSeconds)
    {
        var failedAtUtc = new DateTime(2026, 8, 23, 10, 0, 0, DateTimeKind.Utc);

        var blockedUntilUtc = PortalPinCooldown.GetBlockedUntilUtc(attempts, failedAtUtc);

        if (expectedSeconds == 0)
        {
            blockedUntilUtc.ShouldBeNull();
            return;
        }

        blockedUntilUtc.ShouldBe(failedAtUtc.AddSeconds(expectedSeconds));
    }
}
