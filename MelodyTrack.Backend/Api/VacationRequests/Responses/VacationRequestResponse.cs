namespace MelodyTrack.Backend.Api.VacationRequests.Responses;

public sealed class VacationRequestResponse
{
    public required Ulid Id { get; init; }
    public required string RequesterType { get; init; }
    public required Ulid RequesterId { get; init; }
    public required string RequesterName { get; init; }
    public required string SubjectType { get; init; }
    public required Ulid SubjectId { get; init; }
    public required string SubjectName { get; init; }
    public required string SubjectClassification { get; init; }
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
    public required string Status { get; init; }
    public string? RequestMessage { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public DateTime? ProcessedAtUtc { get; init; }
    public Ulid? ProcessedBySuperuserId { get; init; }
    public string? DecisionMessage { get; init; }
    public Ulid? ResultingVacationId { get; init; }
    public required int Version { get; init; }
    public required IReadOnlyList<VacationPeriodResponse> ExistingVacations { get; init; }
    public required int ConflictingAppointmentCount { get; init; }
}

public sealed class VacationPeriodResponse
{
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
}

public sealed class GetVacationRequestsResponse
{
    public required IReadOnlyList<VacationRequestResponse> Items { get; init; }
}
