using System.ComponentModel.DataAnnotations;
using MelodyTrack.Backend.Data.Enums;

namespace MelodyTrack.Backend.Data.Models;

public sealed class VacationRequest : BaseModel
{
    public required VacationRequestPrincipalType RequesterPrincipalType { get; set; }
    public required Ulid RequesterId { get; set; }
    public required VacationRequestSubjectType SubjectType { get; set; }
    public required Ulid SubjectId { get; set; }
    public required DateOnly RequestedStart { get; set; }
    public required DateOnly RequestedEnd { get; set; }
    public required VacationRequestStatus Status { get; set; }

    [MaxLength(500)]
    public string? RequestMessage { get; set; }

    public required DateTime CreatedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public Ulid? ProcessedBySuperuserId { get; set; }

    [MaxLength(500)]
    public string? DecisionMessage { get; set; }

    public Ulid? ResultingVacationId { get; set; }
    public required int Version { get; set; }
}
