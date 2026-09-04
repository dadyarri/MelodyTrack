using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Backend.Data.Models;

public class Notification : BaseModel
{
    public Ulid? UserId { get; set; }
    public User? User { get; set; }
    public Ulid? ClientId { get; set; }
    public Client? Client { get; set; }

    [MaxLength(100)]
    public required string Type { get; set; }

    [MaxLength(200)]
    public required string Title { get; set; }

    [MaxLength(1000)]
    public required string Summary { get; set; }

    [MaxLength(100)]
    public string? ReferenceType { get; set; }

    public Ulid? ReferenceId { get; set; }

    [MaxLength(1000)]
    public string? DeepLink { get; set; }

    [MaxLength(200)]
    public required string PushMessage { get; set; }

    public required DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public List<NotificationPushDelivery> PushDeliveries { get; set; } = [];
}
