namespace MelodyTrack.Backend.Api.ClientPortal.Requests;

public class AuthenticateSavedClientPortalIdentityRequest
{
    public required string Reference { get; set; }
    public required string Pin { get; set; }
}
