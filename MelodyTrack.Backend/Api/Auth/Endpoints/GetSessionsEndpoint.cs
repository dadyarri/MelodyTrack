using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class GetSessionsEndpoint(AppDbContext db)
    : Ep.NoReq.Res<Results<Ok<GetSessionsResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Get("/auth/sessions");
    }

    public override async Task<Results<Ok<GetSessionsResponse>, UnauthorizedHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);
        var currentSessionIdClaim = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Sid)?.Value;
        var hasCurrentSessionId = Ulid.TryParse(currentSessionIdClaim, out var currentSessionId);

        if (email is null)
        {
            Logger.LogWarning("Session list request without valid email claim in token");
            return TypedResults.Unauthorized();
        }

        var user = await db.Users.AsNoTracking().WhereEmailMatches(email.Value).FirstOrDefaultAsync(ct);

        if (user is null)
        {
            Logger.LogWarning("Session list request for non-existent {EmailRef}", UserUtils.DescribeEmailForLogs(email.Value));
            return TypedResults.Unauthorized();
        }

        var sessions = await db.Sessions
            .AsNoTracking()
            .Where(e => e.User.Id == user.Id && !e.WasRevoked && e.ValidUntil >= DateTime.UtcNow)
            .OrderByDescending(e => e.Id)
            .ToListAsync(ct);

        var data = sessions
            .GroupBy(e => string.IsNullOrWhiteSpace(e.DeviceInfo) ? "Неизвестное устройство" : e.DeviceInfo.Trim())
            .Select(group => group
                .OrderByDescending(session => session.Id)
                .First())
            .OrderByDescending(e => e.Id)
            .Select(e => new SessionDto
            {
                Id = e.Id,
                DeviceInfo = e.DeviceInfo,
                IsCurrent = hasCurrentSessionId && e.Id == currentSessionId,
                LastSeenAtUtc = e.Id.Time.UtcDateTime
            })
            .ToList();

        Logger.LogInformation("Retrieved {Count} active sessions for {EmailRef}", sessions.Count, UserUtils.DescribeEmailForLogs(email.Value));
        return TypedResults.Ok(new GetSessionsResponse
        {
            Data = data
        });
    }
}
