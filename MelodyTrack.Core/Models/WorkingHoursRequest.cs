using System.ComponentModel.DataAnnotations;
using MelodyTrack.Backend.Data.Enums;

namespace MelodyTrack.Backend.Data.Models;

public sealed class WorkingHoursRequest : BaseModel
{
    public required Ulid RequesterUserId { get; set; }
    public required Ulid SubjectUserId { get; set; }
    public required VacationRequestStatus Status { get; set; }

    [MaxLength(500)]
    public string? RequestMessage { get; set; }

    public required DateTime CreatedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public Ulid? ProcessedBySuperuserId { get; set; }

    [MaxLength(500)]
    public string? DecisionMessage { get; set; }

    public required int Version { get; set; }
    public List<WorkingHoursRequestDay> RequestedWorkingHours { get; set; } = [];
}
