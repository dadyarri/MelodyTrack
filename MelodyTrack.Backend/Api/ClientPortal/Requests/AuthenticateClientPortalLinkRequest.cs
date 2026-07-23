namespace MelodyTrack.Backend.Api.ClientPortal.Requests;

public class AuthenticateClientPortalLinkRequest
{
    public required string Token { get; set; }
    public required string Pin { get; set; }
    public string? PinConfirmation { get; set; }
}
