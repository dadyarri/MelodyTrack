namespace MelodyTrack.Backend.Api.WorkingHoursRequests.Responses;

public sealed class WorkingHoursRequestResponse
{
    public required Ulid Id { get; init; }
    public required Ulid RequesterUserId { get; init; }
    public required string RequesterName { get; init; }
    public required Ulid SubjectUserId { get; init; }
    public required string SubjectName { get; init; }
    public required string SubjectClassification { get; init; }
    public required string Status { get; init; }
    public string? RequestMessage { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public DateTime? ProcessedAtUtc { get; init; }
    public Ulid? ProcessedBySuperuserId { get; init; }
    public string? DecisionMessage { get; init; }
    public required int Version { get; init; }
    public required IReadOnlyList<WorkingHoursRequestDayResponse> RequestedWorkingHours { get; init; }
    public required IReadOnlyList<WorkingHoursRequestDayResponse> CurrentWorkingHours { get; init; }
}

public sealed class WorkingHoursRequestDayResponse
{
    public required string DayOfWeek { get; init; }
    public required bool IsWorkingDay { get; init; }
    public string? StartTime { get; init; }
    public string? EndTime { get; init; }
}

public sealed class GetWorkingHoursRequestsResponse
{
    public required IReadOnlyList<WorkingHoursRequestResponse> Items { get; init; }
}
