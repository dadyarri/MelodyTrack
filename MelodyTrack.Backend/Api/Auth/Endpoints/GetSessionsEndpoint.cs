using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Common.Api.Auth.Responses;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Data;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class GetSessionsEndpoint(AppDbContext db)
    : Ep.NoReq.Res<IResult>
{
    public override void Configure()
    {
        Get("/auth/sessions");
    }

    public override async Task<IResult> ExecuteAsync(
        CancellationToken ct)
    {
        var email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

        if (email is null)
        {
            Logger.LogWarning("Session list request without valid email claim in token");
            return ApiResults.Unauthorized();
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(e => e.Email == email.Value, ct);

        if (user is null)
        {
            Logger.LogWarning("Session list request for non-existent user with email {Email}", email.Value);
            return ApiResults.Unauthorized();
        }

        var sessions = await db.Sessions
            .Where(e => e.User.Id == user.Id && !e.WasRevoked && e.ValidUntil >= DateTime.UtcNow)
            .Select(e => new SessionDto
            {
                Id = e.Id,
                DeviceInfo = e.DeviceInfo
            })
            .ToListAsync(ct);

        Logger.LogInformation("Retrieved {Count} active sessions for user {Email}", sessions.Count, email.Value);
        return ApiResults.Ok(new GetSessionsResponse
        {
            Sessions = sessions
        });
    }
}