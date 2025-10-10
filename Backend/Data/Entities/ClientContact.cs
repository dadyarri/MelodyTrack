using System.ComponentModel.DataAnnotations;

namespace Backend.Data.Entities;

public class ClientContact : BaseModel
{
    [Url] public string? Vk { get; set; }

    [Url] public string? Telegram { get; set; }

    [RegularExpression(@"^[\+]?[(]?[0-9]{3}[)]?[-\s\.]?[0-9]{3}[-\s\.]?[0-9]{4,6}$",
        ErrorMessage = "Invalid phone number")]
    public string? Phone { get; set; }

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