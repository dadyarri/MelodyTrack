using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.Notifications.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Core.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MelodyTrack.Backend.Api.Notifications.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/notifications/push/subscription")]
public sealed class UpsertPushSubscriptionEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.StaffOrClientPortal)]
    public static async Task<Results<NoContent, UnauthorizedHttpResult, Conflict, ValidationProblem>> HandleAsync(
        PushSubscriptionRequest request,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        IOptions<WebPushOptions> options,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return TypedResults.Conflict();
        }

        if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Endpoint)] = ["Адрес push-подписки должен использовать HTTPS."]
            });
        }

        var user = await currentUserAccessor.GetAsync(ct);
        var sessionId = currentUserAccessor.SessionId;
        if (user is null || sessionId is null)
        {
            return TypedResults.Unauthorized();
        }

        var principal = NotificationPrincipal.From(user);
        if (!principal.IsValid)
        {
            return TypedResults.Unauthorized();
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var session = await db.Sessions.FirstOrDefaultAsync(item =>
            item.Id == sessionId && item.User.Id == user.Id && !item.WasRevoked && item.ValidUntil > nowUtc, ct);
        if (session is null)
        {
            return TypedResults.Unauthorized();
        }

        var subscription = await db.PushSubscriptions.FirstOrDefaultAsync(item => item.Endpoint == request.Endpoint, ct);
        if (subscription is null)
        {
            subscription = new PushSubscription
            {
                Id = Ulid.NewUlid(),
                UserId = principal.UserId,
                ClientId = principal.ClientId,
                Session = session,
                SessionId = session.Id,
                Endpoint = request.Endpoint,
                P256Dh = request.P256Dh,
                Auth = request.Auth,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                ExpiresAtUtc = request.ExpiresAtUtc
            };
            await db.PushSubscriptions.AddAsync(subscription, ct);
        }
        else
        {
            if (subscription.UserId != principal.UserId || subscription.ClientId != principal.ClientId)
            {
                await db.NotificationPushDeliveries
                    .Where(delivery => delivery.PushSubscriptionId == subscription.Id)
                    .ExecuteDeleteAsync(ct);
            }

            subscription.UserId = principal.UserId;
            subscription.ClientId = principal.ClientId;
            subscription.Session = session;
            subscription.SessionId = session.Id;
            subscription.P256Dh = request.P256Dh;
            subscription.Auth = request.Auth;
            subscription.UpdatedAtUtc = nowUtc;
            subscription.ExpiresAtUtc = request.ExpiresAtUtc;
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
