namespace MelodyTrack.Backend.Api.Auth.Requests;

public class ResetPasswordRequest
{
    public required string Token { get; set; }
    public required string NewPassword { get; set; }
    public string? Otp { get; set; }
}