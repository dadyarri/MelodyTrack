namespace MelodyTrack.Backend.Api.Auth.Responses;

public class RegisterResponse
{
    public bool TotpRequired { get; set; }
    public string? Secret { get; set; }
    public string? OtpUrl { get; set; }
}