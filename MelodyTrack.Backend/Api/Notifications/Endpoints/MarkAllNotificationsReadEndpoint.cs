using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Notifications.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/notifications/read-all")]
public sealed class MarkAllNotificationsReadEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.StaffOrClientPortal)]
    public static async Task<Results<NoContent, UnauthorizedHttpResult>> HandleAsync(
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
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        await db.Notifications
            .Where(notification =>
                notification.ReadAtUtc == null &&
                (principal.UserId != null && notification.UserId == principal.UserId ||
                 principal.ClientId != null && notification.ClientId == principal.ClientId))
            .ExecuteUpdateAsync(setters => setters.SetProperty(notification => notification.ReadAtUtc, nowUtc), ct);
        return TypedResults.NoContent();
    }
}
