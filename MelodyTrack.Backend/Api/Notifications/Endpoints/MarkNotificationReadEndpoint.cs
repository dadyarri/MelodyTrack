using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.Notifications.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Notifications.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/notifications/{id}/read")]
public sealed class MarkNotificationReadEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.StaffOrClientPortal)]
    public static async Task<Results<NoContent, UnauthorizedHttpResult, NotFound>> HandleAsync(
        [AsParameters] NotificationIdRequest request,
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
        var notification = await db.Notifications.FirstOrDefaultAsync(item =>
            item.Id == request.Id &&
            (principal.UserId != null && item.UserId == principal.UserId ||
             principal.ClientId != null && item.ClientId == principal.ClientId), ct);
        if (notification is null)
        {
            return TypedResults.NotFound();
        }

        notification.ReadAtUtc ??= timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
