namespace MelodyTrack.Backend.Api.Auth.Requests;

public class CreateInviteRequest
{
    public string? Email { get; set; }
    public required Ulid Role { get; set; }
}
