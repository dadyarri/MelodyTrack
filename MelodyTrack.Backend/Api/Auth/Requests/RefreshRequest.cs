namespace MelodyTrack.Backend.Api.Auth.Requests;

public class RefreshRequest
{
    public required string RefreshToken { get; set; }
}