using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public sealed class SessionSecurityMonitor(AppDbContext db, IAuditLogService auditLogService, TimeProvider timeProvider)
{
    private const int UnusualActiveSessionThreshold = 5;

    public async Task AuditFanOutIfUnusualAsync(User user, CancellationToken ct)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var activeSessionCount = await db.Sessions
            .AsNoTracking()
            .CountAsync(item => item.User.Id == user.Id && !item.WasRevoked && item.ValidUntil >= nowUtc, ct);

        if (activeSessionCount < UnusualActiveSessionThreshold)
        {
            return;
        }

        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Event = MelodyTrack.Core.Auditing.AuditCatalog.Events.UnusualSessionFanout,
            EntityType = "user",
            EntityId = user.Id.ToString(),
            ActorUserId = user.Id,
            ActorEmail = user.Email,
            ActorDisplayName = $"{user.LastName} {user.FirstName}".Trim(),
            Details = $"Одновременно активных сессий: {activeSessionCount}"
        }, ct);
    }
}
