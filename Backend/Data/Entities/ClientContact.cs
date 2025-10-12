using System.ComponentModel.DataAnnotations;

namespace Backend.Data.Entities;

/// <summary>
///     Контакт пользователя
/// </summary>
public class ClientContact : BaseModel
{
    /// <summary>
    ///     Ссылка на ВК
    /// </summary>
    [Url]
    [MaxLength(100)]
    public string? Vk { get; set; }

    /// <summary>
    ///     Ссылка на телеграм
    /// </summary>
    [Url]
    [MaxLength(100)]
    public string? Telegram { get; set; }

    /// <summary>
    ///     Номер телефона
    /// </summary>
    [RegularExpression(@"^[\+]?[(]?[0-9]{3}[)]?[-\s\.]?[0-9]{3}[-\s\.]?[0-9]{4,6}$",
        ErrorMessage = "Invalid phone number")]
    [MaxLength(20)]
    public string? Phone { get; set; }

    /// <inheritdoc />
    public override string ToString()
    {
        string FormatUrl(string? url, string fieldName)
        {
            if (url == null) return string.Empty;
            var lastPart = url.TrimEnd('/').Split('/').Last();
            return $"{fieldName}: @{lastPart}";
        }

        if (!string.IsNullOrEmpty(Vk))
            return FormatUrl(Vk, "vk");
        if (!string.IsNullOrEmpty(Telegram))
            return FormatUrl(Telegram, "tg");
        if (!string.IsNullOrEmpty(Phone))
            return $"phone: {Phone}";

        return "[Нет контактов]";
    }
}