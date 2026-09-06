namespace MelodyTrack.Backend.Data.Models;

public class ClientPortalLoginLink : BaseModel
{
    public required Ulid UserId { get; set; }
    public required User User { get; set; }
    public string? TokenHash { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
