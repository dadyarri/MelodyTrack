using System.Net;
using System.Text.Json;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Core.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebPush;

namespace MelodyTrack.Backend.Notifications;

public sealed class PushDeliveryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WebPushOptions> options,
    TimeProvider timeProvider,
    WebPushClient client,
    NotificationTelemetry telemetry,
    ILogger<PushDeliveryWorker> logger) : BackgroundService
{
    private const int BatchSize = 25;
    private const int MaximumAttempts = 3;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Web Push delivery is disabled");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Web Push delivery batch failed");
            }

            await Task.Delay(PollInterval, timeProvider, stoppingToken);
        }
    }

    internal async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var deliveries = await db.NotificationPushDeliveries
            .Include(delivery => delivery.Notification)
            .Include(delivery => delivery.PushSubscription)
                .ThenInclude(subscription => subscription.Session)
            .Where(delivery =>
                delivery.Status == NotificationPushDeliveryStatus.Pending &&
                delivery.NextAttemptAtUtc <= nowUtc &&
                delivery.AttemptCount < MaximumAttempts)
            .OrderBy(delivery => delivery.NextAttemptAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var delivery in deliveries)
        {
            await DeliverAsync(db, delivery, cancellationToken);
        }
    }

    private async Task DeliverAsync(
        AppDbContext db,
        NotificationPushDelivery delivery,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var subscription = delivery.PushSubscription;
        if (subscription.Session.WasRevoked || subscription.Session.ValidUntil <= nowUtc ||
            subscription.ExpiresAtUtc <= nowUtc || delivery.Notification.ExpiresAtUtc <= nowUtc)
        {
            delivery.Status = NotificationPushDeliveryStatus.Failed;
            delivery.FailedAtUtc = nowUtc;
            delivery.LastFailureCode = "expired";
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        delivery.AttemptCount++;
        delivery.NextAttemptAtUtc = nowUtc + GetRetryDelay(delivery.AttemptCount);
        await db.SaveChangesAsync(cancellationToken);

        telemetry.RecordPushAttempted();
        try
        {
            var payload = JsonSerializer.Serialize(new PushPayload(
                delivery.Notification.Id.ToString(),
                "MelodyTrack",
                delivery.Notification.PushMessage,
                delivery.Notification.DeepLink ?? "/"));
            var pushSubscription = new WebPush.PushSubscription(subscription.Endpoint, subscription.P256Dh, subscription.Auth);
            var vapid = new VapidDetails(options.Value.Subject, options.Value.PublicKey, options.Value.PrivateKey);

            await client.SendNotificationAsync(pushSubscription, payload, vapid, cancellationToken);

            var deliveredAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            delivery.Status = NotificationPushDeliveryStatus.Delivered;
            delivery.DeliveredAtUtc = deliveredAtUtc;
            delivery.LastFailureCode = null;
            await db.SaveChangesAsync(cancellationToken);
            telemetry.RecordPushSucceeded(deliveredAtUtc - delivery.Notification.CreatedAtUtc);
        }
        catch (WebPushException exception) when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            telemetry.RecordPushFailed();
            telemetry.RecordInvalidSubscriptionRemoved();
            logger.LogInformation(
                "Removed permanently invalid Web Push subscription {SubscriptionId} after delivery {DeliveryId}",
                subscription.Id,
                delivery.Id);
            db.PushSubscriptions.Remove(subscription);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is WebPushException or HttpRequestException or TaskCanceledException)
        {
            telemetry.RecordPushFailed();
            var failureCode = exception is WebPushException webPushException
                ? $"http-{(int)webPushException.StatusCode}"
                : "transport";
            delivery.LastFailureCode = failureCode;
            if (delivery.AttemptCount >= MaximumAttempts)
            {
                delivery.Status = NotificationPushDeliveryStatus.Failed;
                delivery.FailedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            }

            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning(
                "Web Push delivery {DeliveryId} failed with {FailureCode} on attempt {AttemptCount}",
                delivery.Id,
                failureCode,
                delivery.AttemptCount);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            telemetry.RecordPushFailed();
            delivery.Status = NotificationPushDeliveryStatus.Failed;
            delivery.FailedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            delivery.LastFailureCode = "invalid-subscription";
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning(
                "Web Push delivery {DeliveryId} has invalid subscription key material",
                delivery.Id);
        }
    }

    private static TimeSpan GetRetryDelay(int attemptCount) => attemptCount switch
    {
        1 => TimeSpan.FromSeconds(30),
        2 => TimeSpan.FromMinutes(5),
        _ => TimeSpan.Zero
    };

    private sealed record PushPayload(string NotificationId, string Title, string Body, string Url);
}
