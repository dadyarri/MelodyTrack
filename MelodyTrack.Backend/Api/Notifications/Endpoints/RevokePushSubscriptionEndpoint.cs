using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.Notifications.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Notifications.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/notifications/push/subscription/revoke")]
public sealed class RevokePushSubscriptionEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.StaffOrClientPortal)]
    public static async Task<Results<NoContent, UnauthorizedHttpResult>> HandleAsync(
        RevokePushSubscriptionRequest request,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        CancellationToken ct)
    {
        var user = await currentUserAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var principal = NotificationPrincipal.From(user);
        await db.PushSubscriptions
            .Where(subscription =>
                subscription.Endpoint == request.Endpoint &&
                (principal.UserId != null && subscription.UserId == principal.UserId ||
                 principal.ClientId != null && subscription.ClientId == principal.ClientId))
            .ExecuteDeleteAsync(ct);
        return TypedResults.NoContent();
    }
}
