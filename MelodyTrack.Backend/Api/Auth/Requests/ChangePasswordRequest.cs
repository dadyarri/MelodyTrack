using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.Auth.Requests;

public class ChangePasswordRequest : IValidatableRequest
{
    public required string CurrentPassword { get; set; }
    public required string NewPassword { get; set; }
}
