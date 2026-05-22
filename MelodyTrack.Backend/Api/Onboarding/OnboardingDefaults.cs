using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Onboarding;

public static class OnboardingDefaults
{
    public const string InitialStep = "welcome";
    public const string InitialPath = "/";

    public static UserOnboardingState CreateState(User user)
    {
        var now = DateTime.UtcNow;

        return new UserOnboardingState
        {
            Id = Ulid.NewUlid(),
            UserId = user.Id,
            User = user,
            CurrentStep = InitialStep,
            CurrentPath = InitialPath,
            Status = OnboardingStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }
}
