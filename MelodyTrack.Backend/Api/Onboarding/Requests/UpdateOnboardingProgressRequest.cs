namespace MelodyTrack.Backend.Api.Onboarding.Requests;

public class UpdateOnboardingProgressRequest
{
    public string? CurrentStep { get; set; }
    public string? CurrentPath { get; set; }
}
