namespace MelodyTrack.Backend.Api.Auth.Requests;

public class LoginRequest
{
    public required string Email { get; set; }
    public required string Password { get; set; }
    public required string DeviceInfo { get; set; }
}