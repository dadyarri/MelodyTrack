using System.Diagnostics.Metrics;

namespace MelodyTrack.Backend.Notifications;

public sealed class NotificationTelemetry : IDisposable
{
    public const string MeterName = "MelodyTrack.Notifications";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _notificationsCreated;
    private readonly Counter<long> _pushAttempted;
    private readonly Counter<long> _pushSucceeded;
    private readonly Counter<long> _pushFailed;
    private readonly Counter<long> _invalidSubscriptionsRemoved;
    private readonly Histogram<double> _deliveryLatency;

    public NotificationTelemetry()
    {
        _notificationsCreated = _meter.CreateCounter<long>("melodytrack.notifications.created");
        _pushAttempted = _meter.CreateCounter<long>("melodytrack.notifications.push.attempted");
        _pushSucceeded = _meter.CreateCounter<long>("melodytrack.notifications.push.succeeded");
        _pushFailed = _meter.CreateCounter<long>("melodytrack.notifications.push.failed");
        _invalidSubscriptionsRemoved = _meter.CreateCounter<long>("melodytrack.notifications.push.invalid_subscriptions_removed");
        _deliveryLatency = _meter.CreateHistogram<double>("melodytrack.notifications.push.delivery_latency", "s");
    }

    public void RecordNotificationCreated() => _notificationsCreated.Add(1);
    public void RecordPushAttempted() => _pushAttempted.Add(1);
    public void RecordPushSucceeded(TimeSpan latency)
    {
        _pushSucceeded.Add(1);
        _deliveryLatency.Record(latency.TotalSeconds);
    }

    public void RecordPushFailed() => _pushFailed.Add(1);
    public void RecordInvalidSubscriptionRemoved() => _invalidSubscriptionsRemoved.Add(1);
    public void Dispose() => _meter.Dispose();
}
