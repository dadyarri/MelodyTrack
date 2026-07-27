using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Onboarding;

public static class OnboardingDefaults
{
    public const int CurrentDefinitionVersion = 2;
    public const string InitialStep = "welcome";
    public const string InitialPath = "/";

    public static UserOnboardingState CreateState(User user, TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        return new UserOnboardingState
        {
            Id = Ulid.NewUlid(),
            UserId = user.Id,
            User = user,
            CurrentStep = InitialStep,
            CurrentPath = InitialPath,
            DefinitionVersion = CurrentDefinitionVersion,
            Status = OnboardingStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }
}
