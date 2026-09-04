namespace MelodyTrack.Backend.Api.ClientPortal.Responses;

public class ClientPortalAuthenticationResponse
{
    public required string AccessToken { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required SavedClientPortalIdentityResponse SavedIdentity { get; set; }
}

public class SavedClientPortalIdentityResponse
{
    public required string IdentityId { get; set; }
    public required string Reference { get; set; }
    public required string DisplayLabel { get; set; }
    public DateTime LastUsedAtUtc { get; set; }
}
