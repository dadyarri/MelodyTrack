using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MelodyTrack.Backend.Api.Auth.PreProcessors;

public sealed class ActiveSessionPreProcessor : GlobalPreProcessor<ActiveSessionPreProcessor.State>
{
    public sealed class State;

    public override async Task PreProcessAsync(IPreProcessorContext context, State state, CancellationToken ct)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var sessionIdClaim = context.HttpContext.User.Claims.FirstOrDefault(e => e.Type == ClaimTypes.Sid)?.Value;
        if (!Ulid.TryParse(sessionIdClaim, out var sessionId))
        {
            return;
        }

        var services = context.HttpContext.RequestServices;
        var db = services.GetRequiredService<AppDbContext>();
        var logger = services.GetRequiredService<ILogger<ActiveSessionPreProcessor>>();

        var isSessionActive = await db.Sessions
            .AsNoTracking()
            .AnyAsync(e => e.Id == sessionId && !e.WasRevoked && e.ValidUntil >= DateTime.UtcNow, ct);

        if (isSessionActive)
        {
            return;
        }

        logger.LogWarning(
            "Authenticated request with inactive session {SessionId} to {Method} {Path}",
            sessionId,
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path);
        await context.HttpContext.Response.SendUnauthorizedAsync(ct);
    }
}
