namespace MelodyTrack.Backend.Api.Auth.Responses;

public class ForgotPasswordResponse
{
    public required string Token { get; set; }
    public required string Url { get; set; }
}
