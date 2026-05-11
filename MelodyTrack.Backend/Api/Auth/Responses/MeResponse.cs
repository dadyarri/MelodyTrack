namespace MelodyTrack.Backend.Api.Auth.Responses;

public class MeResponse
{
    public required string Email { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string RoleDisplayName { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsSuperuser { get; set; }
    public bool IsTwoFactorEnabled { get; set; }
    public bool IsTwoFactorRequired { get; set; }
}
