using FastEndpoints;

namespace MelodyTrack.Backend.Api.Users.Requests;

public class UpdateUserRequest
{
    [BindFrom("id")]
    public Ulid Id { get; set; }
    public Ulid? ExpectedActivityId { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Phone { get; set; }
    public string? Telegram { get; set; }
    public string? Vk { get; set; }
}
