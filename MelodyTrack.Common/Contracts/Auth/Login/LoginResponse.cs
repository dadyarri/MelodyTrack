namespace MelodyTrack.Common.Contracts.Auth.Login;

public class LoginResponse
{
    public required string AccessToken { get; set; }
    public required string RefreshToken { get; set; }
}
