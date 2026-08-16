namespace MelodyTrack.Backend.Data.Models;

public class ClientPortalLoginLink : BaseModel
{
    public required Ulid UserId { get; set; }
    public required User User { get; set; }
    public string? TokenHash { get; set; }
    public string? PinHash { get; set; }
    public DateTime? PinSetAtUtc { get; set; }
    public int FailedPinAttempts { get; set; }
    public DateTime? LastFailedPinAttemptAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public List<ClientPortalSavedIdentityReference> SavedIdentityReferences { get; set; } = [];
}
