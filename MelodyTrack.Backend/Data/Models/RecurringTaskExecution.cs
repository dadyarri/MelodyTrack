using System.ComponentModel.DataAnnotations;
using MelodyTrack.Backend.Data.Enums;

namespace MelodyTrack.Backend.Data.Models;

public class RecurringTaskExecution : BaseModel
{
    public required Ulid RuleId { get; set; }
    public required RecurringTaskRule Rule { get; set; }

    public required RecurringTaskStatus Status { get; set; }

    public required RecurringTaskRecipientType RecipientType { get; set; }

    public Ulid? ClientId { get; set; }
    public Client? Client { get; set; }

    public Ulid? TeacherId { get; set; }
    public User? Teacher { get; set; }

    public Ulid? AppointmentId { get; set; }
    public Appointment? Appointment { get; set; }

    public required DateOnly BusinessDate { get; set; }

    [MaxLength(500)]
    public required string DeduplicationKey { get; set; }

    [MaxLength(2000)]
    public string? GeneratedText { get; set; }

    public Ulid? CompletedByUserId { get; set; }
    public User? CompletedByUser { get; set; }

    public Ulid? SkippedByUserId { get; set; }
    public User? SkippedByUser { get; set; }

    public required DateTime CreatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public DateTime? SkippedAtUtc { get; set; }
}
