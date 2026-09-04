using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Backend.Data.Models;

public class CustomTask : BaseModel
{
    public Ulid? ClientId { get; set; }
    public Client? Client { get; set; }

    [MaxLength(200)]
    public required string RecipientName { get; set; }

    [MaxLength(100)]
    public string? Phone { get; set; }

    [MaxLength(200)]
    public string? Telegram { get; set; }

    [MaxLength(200)]
    public string? Vk { get; set; }

    [MaxLength(200)]
    public required string Title { get; set; }

    [MaxLength(2000)]
    public required string MessageText { get; set; }

    public required DateTime DueAtUtc { get; set; }

    public required DateTime CreatedAtUtc { get; set; }

    public Ulid? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    public Ulid? CompletedByUserId { get; set; }
    public User? CompletedByUser { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public Ulid? CancelledByUserId { get; set; }
    public User? CancelledByUser { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    public Ulid? DelayedByUserId { get; set; }
    public User? DelayedByUser { get; set; }

    public DateTime? DelayedAtUtc { get; set; }

    public DateTime? DelayedUntilUtc { get; set; }
}
