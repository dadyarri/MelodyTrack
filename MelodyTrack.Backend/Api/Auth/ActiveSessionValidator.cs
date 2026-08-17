using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth;

public sealed class ActiveSessionValidator(
    AppDbContext db,
    ICurrentUserAccessor currentUserAccessor,
    TimeProvider timeProvider,
    ILogger<ActiveSessionValidator> logger)
{
    public async Task<bool> IsActiveAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return true;
        }

        var sessionId = currentUserAccessor.SessionId;
        if (sessionId is null)
        {
            return true;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var isSessionActive = await db.Sessions
            .AsNoTracking()
            .AnyAsync(
                session => session.Id == sessionId.Value && !session.WasRevoked && session.ValidUntil >= nowUtc,
                cancellationToken);

        if (!isSessionActive)
        {
            logger.LogWarning(
                "Authenticated request with inactive session {SessionId} to {Method} {Path}",
                sessionId,
                context.Request.Method,
                context.Request.Path);
        }

        return isSessionActive;
    }
}
