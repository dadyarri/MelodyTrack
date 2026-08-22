using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.Auth.Requests;

public class Recover2FaRequest : IValidatableRequest
{
    public required string Email { get; set; }
    public required string RecoveryCode { get; set; }
}