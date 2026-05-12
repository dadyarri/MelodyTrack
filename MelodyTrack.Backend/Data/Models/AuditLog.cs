using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Backend.Data.Models;

public class AuditLog : BaseModel
{
    public required DateTime CreatedAtUtc { get; set; }

    [MaxLength(50)]
    public required string Category { get; set; }

    [MaxLength(100)]
    public required string Action { get; set; }

    [MaxLength(100)]
    public required string EntityType { get; set; }

    [MaxLength(64)]
    public string? EntityId { get; set; }

    public Ulid? ActorUserId { get; set; }

    [MaxLength(200)]
    public string? ActorEmail { get; set; }

    [MaxLength(200)]
    public string? ActorDisplayName { get; set; }

    [MaxLength(1000)]
    public string? Details { get; set; }
}
