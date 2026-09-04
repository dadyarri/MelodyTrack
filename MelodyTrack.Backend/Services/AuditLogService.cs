using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Core.Auditing;

namespace MelodyTrack.Backend.Services;

public sealed class AuditLogWriteRequest
{
    public required AuditEventDefinition Event { get; init; }
    public required string EntityType { get; init; }
    public string? EntityId { get; init; }
    public string? Details { get; init; }
    public Ulid? ActorUserId { get; init; }
    public string? ActorEmail { get; init; }
    public string? ActorDisplayName { get; init; }
}

public interface IAuditLogService
{
    Task WriteAsync(AuditLogWriteRequest request, CancellationToken ct);
}

public class AuditLogService(
    AppDbContext db,
    IHttpContextAccessor httpContextAccessor,
    ICurrentUserAccessor currentUserAccessor,
    TimeProvider timeProvider) : IAuditLogService
{
    public async Task WriteAsync(AuditLogWriteRequest request, CancellationToken ct)
    {
        var actorUserId = request.ActorUserId;
        var actorEmail = request.ActorEmail;
        var actorDisplayName = request.ActorDisplayName;
        var sourceIpAddress = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

        if (actorUserId is null && string.IsNullOrWhiteSpace(actorEmail))
        {
            var email = currentUserAccessor.Email;

            if (!string.IsNullOrWhiteSpace(email))
            {
                var actor = await currentUserAccessor.GetAsync(ct);

                if (actor is not null)
                {
                    actorUserId = actor.Id;
                    actorEmail = actor.Email;
                    actorDisplayName = $"{actor.LastName} {actor.FirstName}".Trim();
                }
                else
                {
                    actorEmail = email;
                }
            }
        }

        var auditLog = new AuditLog
        {
            Id = Ulid.NewUlid(),
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            Category = request.Event.Category.Code,
            Action = request.Event.Code,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            ActorUserId = actorUserId,
            ActorEmail = actorEmail,
            ActorDisplayName = actorDisplayName,
            SourceIpAddress = sourceIpAddress,
            Details = request.Details
        };

        await db.AuditLogs.AddAsync(auditLog, ct);
        await db.SaveChangesAsync(ct);
    }
}
