namespace MelodyTrack.Backend.Api.Auth.Responses;

public class Recover2FaResponse
{
    public required string AccessToken { get; set; }
    public required string RefreshToken { get; set; }
    public required string Secret { get; set; }
    public required string OtpUrl { get; set; }
    public required List<string> Codes { get; set; }
    public required List<RecoveryCodeDto> AllCodes { get; set; }
}
