namespace MelodyTrack.Backend.Api.Auth.Responses;

using MelodyTrack.Backend.Api.Common.Responses;

public class MeResponse
{
    public required Ulid Id { get; set; }
    public required string Email { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string RoleDisplayName { get; set; }
    public string? Phone { get; set; }
    public string? Telegram { get; set; }
    public string? Vk { get; set; }
    public RecordActivityDto? LastActivity { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsSuperuser { get; set; }
    public bool IsTwoFactorEnabled { get; set; }
    public bool IsTwoFactorRequired { get; set; }
}
