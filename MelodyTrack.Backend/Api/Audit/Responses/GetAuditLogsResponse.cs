using MelodyTrack.Backend.Api.Common.Responses;

namespace MelodyTrack.Backend.Api.Audit.Responses;

public class GetAuditLogsResponse : PaginatedResponse<GetAuditLogsDto>
{
}

public class GetAuditLogsDto
{
    public required Ulid Id { get; set; }
    public required DateTime CreatedAtUtc { get; set; }
    public required string Category { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? ActorEmail { get; set; }
    public string? ActorDisplayName { get; set; }
    public string? Details { get; set; }
}
