namespace MelodyTrack.Backend.Api.SystemNotices.Responses;

public sealed class SystemNoticeResponse
{
    public required Ulid Id { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public required string Severity { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public required bool Dismissible { get; init; }
    public required string AudienceType { get; init; }
    public required bool ShowBeforeAuthentication { get; init; }
}

public sealed class GetSystemNoticesResponse
{
    public required IReadOnlyList<SystemNoticeResponse> Items { get; init; }
}
