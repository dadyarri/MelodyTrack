using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.Auth.Requests;

public class Setup2FaRequest : IValidatableRequest
{
    public required string Password { get; set; }
}