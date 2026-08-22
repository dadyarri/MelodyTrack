using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.Auth.Requests;

public class LoginRequest : IValidatableRequest
{
    public required string Email { get; set; }
    public required string Password { get; set; }
    public string? Otp { get; set; }
    public string? RecoveryCode { get; set; }
}
