namespace MelodyTrack.Backend.Api.Auth.Responses;

public class Setup2FaResponse
{
    public string? Secret { get; set; }
    public string? OtpUrl { get; set; }
}