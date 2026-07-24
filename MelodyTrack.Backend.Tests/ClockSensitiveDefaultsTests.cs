using System.Runtime.CompilerServices;
using MelodyTrack.Backend.Api.Onboarding;
using MelodyTrack.Backend.Data.Models;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public class ClockSensitiveDefaultsTests
{
    [Fact]
    public void CreateOnboardingState_UsesInjectedClockForBothTimestamps()
    {
        var now = new DateTimeOffset(2026, 7, 24, 12, 34, 56, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(now);
        var user = (User)RuntimeHelpers.GetUninitializedObject(typeof(User));
        user.Id = Ulid.NewUlid();

        var state = OnboardingDefaults.CreateState(user, timeProvider);

        state.CreatedAtUtc.ShouldBe(now.UtcDateTime);
        state.UpdatedAtUtc.ShouldBe(now.UtcDateTime);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
