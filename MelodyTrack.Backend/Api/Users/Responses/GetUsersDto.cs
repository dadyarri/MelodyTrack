namespace MelodyTrack.Backend.Api.Users.Responses;

public class GetUsersDto
{
    public required Ulid Id { get; set; }
    public required string LastName { get; set; }
    public required string FirstName { get; set; }
    public required string RoleDisplayName { get; set; }
    public string? Telegram { get; set; }
    public string? Vk { get; set; }
    public string? Phone { get; set; }
}
