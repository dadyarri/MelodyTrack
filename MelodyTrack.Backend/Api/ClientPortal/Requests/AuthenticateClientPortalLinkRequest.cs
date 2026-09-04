using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.ClientPortal.Requests;

public class AuthenticateClientPortalLinkRequest : IValidatableRequest
{
    public required string Token { get; set; }
    public required string Pin { get; set; }
    public string? PinConfirmation { get; set; }
}
