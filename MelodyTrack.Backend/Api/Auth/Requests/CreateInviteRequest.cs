using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.Auth.Requests;

public class CreateInviteRequest : IValidatableRequest
{
    public string? Email { get; set; }
    public required Ulid Role { get; set; }
}
