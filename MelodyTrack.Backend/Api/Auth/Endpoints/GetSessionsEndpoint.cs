using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class GetSessionsEndpoint(AppDbContext db, TimeProvider timeProvider, ICurrentUserAccessor currentUserAccessor)
    : Ep.NoReq.Res<Results<Ok<GetSessionsResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Get("/auth/sessions");
    }

    public override async Task<Results<Ok<GetSessionsResponse>, UnauthorizedHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var user = await currentUserAccessor.GetAsync(ct);
        if (user is null)
        {
            Logger.LogWarning("Session list request without a current user");
            return TypedResults.Unauthorized();
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var sessions = await db.Sessions
            .AsNoTracking()
            .Where(e => e.User.Id == user.Id && !e.WasRevoked && e.ValidUntil >= nowUtc)
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
                IsCurrent = e.Id == currentUserAccessor.SessionId,
                LastSeenAtUtc = e.Id.Time.UtcDateTime
            })
            .ToList();

        Logger.LogInformation("Retrieved {Count} active sessions for {EmailRef}", sessions.Count, UserUtils.DescribeEmailForLogs(user.Email));
        return TypedResults.Ok(new GetSessionsResponse
        {
            Data = data
        });
    }
}
