using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.ClientPortal.Requests;

public class AuthenticateSavedClientPortalIdentityRequest : IValidatableRequest
{
    public required string Reference { get; set; }
    public required string Pin { get; set; }
}
