using System.Security.Claims;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public sealed class AuditLogWriteRequest
{
    public required string Category { get; init; }
    public required string Action { get; init; }
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

public class AuditLogService(AppDbContext db, IHttpContextAccessor httpContextAccessor) : IAuditLogService
{
    public async Task WriteAsync(AuditLogWriteRequest request, CancellationToken ct)
    {
        var actorUserId = request.ActorUserId;
        var actorEmail = request.ActorEmail;
        var actorDisplayName = request.ActorDisplayName;

        if (actorUserId is null && string.IsNullOrWhiteSpace(actorEmail))
        {
            var email = httpContextAccessor.HttpContext?.User.Claims
                .FirstOrDefault(claim => claim.Type == ClaimTypes.Name)
                ?.Value;

            if (!string.IsNullOrWhiteSpace(email))
            {
                var actor = await db.Users
                    .AsNoTracking()
                    .Where(user => user.Email == email)
                    .Select(user => new
                    {
                        user.Id,
                        user.Email,
                        user.FirstName,
                        user.LastName
                    })
                    .FirstOrDefaultAsync(ct);

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
            CreatedAtUtc = DateTime.UtcNow,
            Category = request.Category,
            Action = request.Action,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            ActorUserId = actorUserId,
            ActorEmail = actorEmail,
            ActorDisplayName = actorDisplayName,
            Details = request.Details
        };

        await db.AuditLogs.AddAsync(auditLog, ct);
        await db.SaveChangesAsync(ct);
    }
}
