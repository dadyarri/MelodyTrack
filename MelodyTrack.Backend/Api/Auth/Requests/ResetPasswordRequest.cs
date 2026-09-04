using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.Auth.Requests;

public class ResetPasswordRequest : IValidatableRequest
{
    public required string Token { get; set; }
    public required string NewPassword { get; set; }
    public string? Otp { get; set; }
    public string? RecoveryCode { get; set; }
}
