using MelodyTrack.Backend.Api.Onboarding.Responses;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Onboarding;

public static class OnboardingStateMapper
{
    public static OnboardingStateResponse ToResponse(UserOnboardingState state)
    {
        return new OnboardingStateResponse
        {
            Status = MapStatus(state.Status),
            CurrentStep = state.CurrentStep,
            CurrentPath = state.CurrentPath,
            ShouldLaunch = state.Status == OnboardingStatus.Active,
            UpdatedAtUtc = state.UpdatedAtUtc,
            CompletedAtUtc = state.CompletedAtUtc
        };
    }

    private static string MapStatus(OnboardingStatus status)
    {
        return status switch
        {
            OnboardingStatus.Completed => "completed",
            OnboardingStatus.Skipped => "skipped",
            _ => "active"
        };
    }
}
