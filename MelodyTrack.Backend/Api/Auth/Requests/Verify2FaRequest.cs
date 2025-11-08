namespace MelodyTrack.Backend.Api.Auth.Requests;

public class Verify2FaRequest
{
    public string? Email { get; set; }
    public required string Otp { get; set; }
    public required string OtpSecret { get; set; }
}