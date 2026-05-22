namespace MelodyTrack.Backend.Api.Onboarding.Responses;

public class OnboardingStateResponse
{
    public required string Status { get; set; }
    public required string CurrentStep { get; set; }
    public required string CurrentPath { get; set; }
    public bool ShouldLaunch { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
