using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Backend.Api.Notifications.Requests;

public sealed class PushSubscriptionRequest
{
    [Required(ErrorMessage = "Адрес push-подписки обязателен.")]
    [MaxLength(2048, ErrorMessage = "Адрес push-подписки слишком длинный.")]
    public required string Endpoint { get; set; }

    [Required(ErrorMessage = "Ключ push-подписки обязателен.")]
    [MaxLength(512, ErrorMessage = "Ключ push-подписки слишком длинный.")]
    public required string P256Dh { get; set; }

    [Required(ErrorMessage = "Ключ аутентификации push-подписки обязателен.")]
    [MaxLength(512, ErrorMessage = "Ключ аутентификации push-подписки слишком длинный.")]
    public required string Auth { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }
}
