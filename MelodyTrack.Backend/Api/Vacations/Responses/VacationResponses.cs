using MelodyTrack.Backend.Api.VacationRequests.Responses;

namespace MelodyTrack.Backend.Api.Vacations.Responses;

public sealed class VacationResponse
{
    public required Ulid Id { get; init; }
    public required string SubjectType { get; init; }
    public required Ulid SubjectId { get; init; }
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public required int Version { get; init; }
    public required string State { get; init; }
    public required string Timezone { get; init; }
    public DateTime? OriginalEndDate { get; init; }
    public DateTime? EndedAtUtc { get; init; }
    public DateTime? DeletedAtUtc { get; init; }
    public required bool RequiresApproval { get; init; }
    public required bool CanManage { get; init; }
    public required bool CanEnd { get; init; }
}

public sealed class GetVacationsResponse
{
    public required IReadOnlyList<VacationResponse> Items { get; init; }
}

public sealed class VacationOperationResponse
{
    public required bool RequiresApproval { get; init; }
    public VacationResponse? Vacation { get; init; }
    public VacationRequestResponse? Request { get; init; }
}

public sealed class VacationRestorationCandidateResponse
{
    public required Ulid Id { get; init; }
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public required string ServiceName { get; init; }
    public required bool Available { get; init; }
    public required IReadOnlyList<string> Blockers { get; init; }
}

public sealed class GetVacationRestorationCandidatesResponse
{
    public required IReadOnlyList<VacationRestorationCandidateResponse> Items { get; init; }
}
