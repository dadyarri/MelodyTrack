namespace MelodyTrack.ApiService.Endpoints.Auth.Login;

public class LoginResponse
{
    public required string AccessToken { get; set; }
    public required string RefreshToken { get; set; }
    public required DateTime ValidUntil { get; set; }
}
