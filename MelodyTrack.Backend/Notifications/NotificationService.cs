using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Notifications;

public sealed record NotificationDraft(
    Ulid? UserId,
    Ulid? ClientId,
    string Type,
    string Title,
    string Summary,
    string PushMessage,
    string? DeepLink = null,
    string? ReferenceType = null,
    Ulid? ReferenceId = null,
    DateTime? ExpiresAtUtc = null);

public interface INotificationService
{
    Task<Notification> CreateAsync(NotificationDraft draft, CancellationToken cancellationToken);
}

public sealed class NotificationService(
    AppDbContext db,
    TimeProvider timeProvider,
    NotificationTelemetry telemetry) : INotificationService
{
    public async Task<Notification> CreateAsync(NotificationDraft draft, CancellationToken cancellationToken)
    {
        ValidateDraft(draft);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var notification = new Notification
        {
            Id = Ulid.NewUlid(),
            UserId = draft.UserId,
            ClientId = draft.ClientId,
            Type = draft.Type,
            Title = draft.Title,
            Summary = draft.Summary,
            PushMessage = draft.PushMessage,
            DeepLink = draft.DeepLink,
            ReferenceType = draft.ReferenceType,
            ReferenceId = draft.ReferenceId,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = draft.ExpiresAtUtc
        };

        var subscriptions = await db.PushSubscriptions
            .Where(subscription =>
                (draft.UserId != null && subscription.UserId == draft.UserId ||
                 draft.ClientId != null && subscription.ClientId == draft.ClientId) &&
                (subscription.ExpiresAtUtc == null || subscription.ExpiresAtUtc > nowUtc) &&
                !subscription.Session.WasRevoked &&
                subscription.Session.ValidUntil > nowUtc)
            .ToListAsync(cancellationToken);

        foreach (var subscription in subscriptions)
        {
            notification.PushDeliveries.Add(new NotificationPushDelivery
            {
                Id = Ulid.NewUlid(),
                Notification = notification,
                NotificationId = notification.Id,
                PushSubscription = subscription,
                PushSubscriptionId = subscription.Id,
                Status = NotificationPushDeliveryStatus.Pending,
                CreatedAtUtc = nowUtc,
                NextAttemptAtUtc = nowUtc
            });
        }

        await db.Notifications.AddAsync(notification, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        telemetry.RecordNotificationCreated();
        return notification;
    }

    private static void ValidateDraft(NotificationDraft draft)
    {
        if ((draft.UserId is null) == (draft.ClientId is null))
        {
            throw new ArgumentException("A notification must have exactly one recipient.", nameof(draft));
        }

        if (string.IsNullOrWhiteSpace(draft.Type) || string.IsNullOrWhiteSpace(draft.Title) ||
            string.IsNullOrWhiteSpace(draft.Summary) || string.IsNullOrWhiteSpace(draft.PushMessage))
        {
            throw new ArgumentException("Notification text and type are required.", nameof(draft));
        }

        if (draft.DeepLink is not null && (!draft.DeepLink.StartsWith('/') || draft.DeepLink.StartsWith("//", StringComparison.Ordinal)))
        {
            throw new ArgumentException("Notification deep links must be local application paths.", nameof(draft));
        }
    }
}
