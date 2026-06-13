namespace MelodyTrack.Backend.Data.Models;

public class ClientPortalLoginLink : BaseModel
{
    public required Ulid UserId { get; set; }
    public required User User { get; set; }
    public required string Token { get; set; }
    public string? PinCode { get; set; }
    public DateTime? PinSetAtUtc { get; set; }
}
