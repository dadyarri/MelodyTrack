namespace MelodyTrack.Common.Api.Auth.Requests;

public class LogoutRequest
{
    public required string RefreshToken { get; set; }
}