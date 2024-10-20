namespace MelodyTrack.Common.Contracts.Auth.Login;

using JetBrains.Annotations;

[UsedImplicitly]
public class LoginRequest
{
    public string Email { get; set; }
    public string Password { get; set; }
}
