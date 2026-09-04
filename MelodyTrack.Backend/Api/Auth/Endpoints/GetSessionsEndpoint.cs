using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/auth/sessions")]
public sealed class GetSessionsEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.StaffOrClientPortal)]
    public static async Task<Results<Ok<GetSessionsResponse>, UnauthorizedHttpResult>> HandleAsync(
        AppDbContext db,
        TimeProvider timeProvider,
        ICurrentUserAccessor currentUserAccessor,
        ILogger<GetSessionsEndpoint> logger,
        CancellationToken ct
    )
    {
        var user = await currentUserAccessor.GetAsync(ct);
        if (user is null)
        {
            logger.LogWarning("Session list request without a current user");
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
                CreatedAtUtc = e.Id.Time.UtcDateTime
            })
            .ToList();

        logger.LogInformation("Retrieved {Count} active sessions for {EmailRef}", sessions.Count, UserUtils.DescribeEmailForLogs(user.Email));
        return TypedResults.Ok(new GetSessionsResponse
        {
            Data = data
        });
    }
}
