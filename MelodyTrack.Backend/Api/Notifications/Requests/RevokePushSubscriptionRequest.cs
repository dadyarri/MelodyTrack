using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Backend.Api.Notifications.Requests;

public sealed class RevokePushSubscriptionRequest
{
    [Required(ErrorMessage = "Адрес push-подписки обязателен.")]
    [MaxLength(2048, ErrorMessage = "Адрес push-подписки слишком длинный.")]
    public required string Endpoint { get; set; }
}
