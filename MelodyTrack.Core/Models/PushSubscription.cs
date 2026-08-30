using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Backend.Data.Models;

public class PushSubscription : BaseModel
{
    public Ulid? UserId { get; set; }
    public User? User { get; set; }
    public Ulid? ClientId { get; set; }
    public Client? Client { get; set; }
    public required Ulid SessionId { get; set; }
    public required Session Session { get; set; }

    [MaxLength(2048)]
    public required string Endpoint { get; set; }

    [MaxLength(512)]
    public required string P256Dh { get; set; }

    [MaxLength(512)]
    public required string Auth { get; set; }

    public required DateTime CreatedAtUtc { get; set; }
    public required DateTime UpdatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public List<NotificationPushDelivery> Deliveries { get; set; } = [];
}
