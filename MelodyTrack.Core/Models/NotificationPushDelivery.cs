using System.ComponentModel.DataAnnotations;
using MelodyTrack.Backend.Data.Enums;

namespace MelodyTrack.Backend.Data.Models;

public class NotificationPushDelivery : BaseModel
{
    public required Ulid NotificationId { get; set; }
    public required Notification Notification { get; set; }
    public required Ulid PushSubscriptionId { get; set; }
    public required PushSubscription PushSubscription { get; set; }
    public required NotificationPushDeliveryStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public required DateTime CreatedAtUtc { get; set; }
    public required DateTime NextAttemptAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public DateTime? FailedAtUtc { get; set; }

    [MaxLength(64)]
    public string? LastFailureCode { get; set; }
}
