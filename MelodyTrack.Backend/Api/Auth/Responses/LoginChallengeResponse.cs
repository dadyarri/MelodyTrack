namespace MelodyTrack.Backend.Api.Auth.Responses;

public class LoginChallengeResponse
{
    public bool RequiresTwoFactor { get; set; }
    public bool CanUseOtp { get; set; }
    public bool CanUseRecoveryCode { get; set; }
}
