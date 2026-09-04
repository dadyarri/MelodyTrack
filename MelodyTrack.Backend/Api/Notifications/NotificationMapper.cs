using MelodyTrack.Backend.Api.Notifications.Responses;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Notifications;

internal static class NotificationMapper
{
    public static NotificationResponse ToResponse(this Notification notification) => new()
    {
        Id = notification.Id,
        Type = notification.Type,
        Title = notification.Title,
        Summary = notification.Summary,
        ReferenceType = notification.ReferenceType,
        ReferenceId = notification.ReferenceId,
        DeepLink = notification.DeepLink,
        CreatedAtUtc = notification.CreatedAtUtc,
        ReadAtUtc = notification.ReadAtUtc,
        ExpiresAtUtc = notification.ExpiresAtUtc
    };
}
