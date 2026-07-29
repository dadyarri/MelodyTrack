using MelodyTrack.Backend.Services;

namespace MelodyTrack.Backend.Api.Releases.Responses;

public sealed record CurrentReleaseResponse(string Version, string Codename);

public sealed record ReleaseResponse(
    string Version,
    string Codename,
    DateOnly Date,
    ReleaseChanges Changes,
    string? ParentVersion);

public sealed record ReleasesResponse(
    string CurrentVersion,
    IReadOnlyList<ReleaseResponse> Releases,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasNextPage);
