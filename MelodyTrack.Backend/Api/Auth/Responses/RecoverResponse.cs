namespace MelodyTrack.Backend.Api.Auth.Responses;

public class RecoverResponse
{
    public required string AccessToken { get; set; }
    public required string RefreshToken { get; set; }
    public required string Secret { get; set; }
    public required string OtpUrl { get; set; }
}