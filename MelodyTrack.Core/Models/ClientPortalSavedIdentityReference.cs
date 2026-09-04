namespace MelodyTrack.Backend.Data.Models;

public class ClientPortalSavedIdentityReference : BaseModel
{
    public required Ulid LoginLinkId { get; set; }
    public required ClientPortalLoginLink LoginLink { get; set; }
    public required string ReferenceHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastUsedAtUtc { get; set; }
}
