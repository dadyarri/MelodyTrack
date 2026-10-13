using System.Text.Json.Serialization;

namespace MelodyTrack.Backend.Data.Models;

public sealed class Vacation : BaseModel
{
    public Ulid? UserId { get; set; }
    [JsonIgnore]
    public User? User { get; set; }
    public Ulid? ClientId { get; set; }
    [JsonIgnore]
    public Client? Client { get; set; }
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
    public DateTime? OriginalEndDate { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public bool IsCancelled { get; set; }
    public int Version { get; set; } = 1;
}
