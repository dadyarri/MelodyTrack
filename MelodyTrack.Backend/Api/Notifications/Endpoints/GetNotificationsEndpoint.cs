using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.Notifications.Requests;
using MelodyTrack.Backend.Api.Notifications.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Notifications.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/notifications")]
public sealed class GetNotificationsEndpoint
{
    private const int HistoryRetentionDays = 90;

    [Authorize(Policy = AuthorizationPolicies.StaffOrClientPortal)]
    public static async Task<Results<Ok<GetNotificationsResponse>, UnauthorizedHttpResult>> HandleAsync(
        [AsParameters] GetNotificationsRequest request,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var user = await currentUserAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var principal = NotificationPrincipal.From(user);
        if (!principal.IsValid)
        {
            return TypedResults.Unauthorized();
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var retainedAfterUtc = nowUtc.AddDays(-HistoryRetentionDays);
        var query = db.Notifications
            .AsNoTracking()
            .Where(notification =>
                (principal.UserId != null && notification.UserId == principal.UserId ||
                 principal.ClientId != null && notification.ClientId == principal.ClientId) &&
                notification.CreatedAtUtc >= retainedAfterUtc &&
                (notification.ExpiresAtUtc == null || notification.ExpiresAtUtc > nowUtc));

        var unreadCount = await query.CountAsync(notification => notification.ReadAtUtc == null, ct);
        if (request.UnreadOnly is true)
        {
            query = query.Where(notification => notification.ReadAtUtc == null);
        }

        var notifications = await query
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .Take(request.EffectiveLimit)
            .ToListAsync(ct);

        return TypedResults.Ok(new GetNotificationsResponse
        {
            Items = notifications.Select(NotificationMapper.ToResponse).ToArray(),
            UnreadCount = unreadCount
        });
    }
}
