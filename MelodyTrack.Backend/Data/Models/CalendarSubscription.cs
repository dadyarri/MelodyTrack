using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Backend.Data.Models;

public class CalendarSubscription : BaseModel
{
    [MaxLength(128)]
    public required string Token { get; set; }
    public Ulid? UserId { get; set; }
    public User? User { get; set; }
    public Ulid? ClientId { get; set; }
    public Client? Client { get; set; }
    public required DateTime CreatedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
