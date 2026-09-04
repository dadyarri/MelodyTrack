using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.Notifications.Requests;

public sealed class GetNotificationsRequest
{
    [FromQuery(Name = "limit")]
    [Range(1, 100, ErrorMessage = "Можно запросить от 1 до 100 уведомлений.")]
    public int? Limit { get; set; }

    [FromQuery(Name = "unreadOnly")]
    public bool? UnreadOnly { get; set; }

    internal int EffectiveLimit => Limit ?? 30;
}
