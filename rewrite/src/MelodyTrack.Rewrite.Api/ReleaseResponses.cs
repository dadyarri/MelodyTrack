namespace MelodyTrack.Rewrite.Api;

public sealed record CurrentReleaseResponse(string Version, string? Codename, string? CodenameMeaning);

public sealed record ReleaseChanges(IReadOnlyList<string> New, IReadOnlyList<string> Improved,
    IReadOnlyList<string> Fixed, IReadOnlyList<string> Security);

public sealed record ReleaseResponse(string Version, string? Codename, string? CodenameMeaning, DateOnly? Date,
    ReleaseChanges Changes, string? ParentVersion);

public sealed record ReleasesResponse(string CurrentVersion, IReadOnlyList<ReleaseResponse> Releases,
    int Page, int PageSize, int TotalCount, int TotalPages, bool HasNextPage);
