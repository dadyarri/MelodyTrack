namespace MelodyTrack.ApiService.Endpoints.Auth.Logout;

public class LogoutRequest
{
    public required string RefreshToken { get; set; }
}
