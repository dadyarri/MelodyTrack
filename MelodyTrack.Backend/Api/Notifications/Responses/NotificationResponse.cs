namespace MelodyTrack.Backend.Api.Notifications.Responses;

public sealed class NotificationResponse
{
    public required Ulid Id { get; init; }
    public required string Type { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public string? ReferenceType { get; init; }
    public Ulid? ReferenceId { get; init; }
    public string? DeepLink { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public DateTime? ReadAtUtc { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
}

public sealed class GetNotificationsResponse
{
    public required IReadOnlyList<NotificationResponse> Items { get; init; }
    public required int UnreadCount { get; init; }
}

public sealed class WebPushConfigurationResponse
{
    public required bool Enabled { get; init; }
    public string? PublicKey { get; init; }
}
