using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Backend.Data.Models;

public class ClientContacts : BaseModel
{
    [Url] [MaxLength(100)] public string? Telegram { get; set; }
    [Url] [MaxLength(100)] public string? Vk { get; set; }

    [RegularExpression(@"^[\+]?[(]?[0-9]{3}[)]?[-\s\.]?[0-9]{3}[-\s\.]?[0-9]{4,6}$",
        ErrorMessage = "Invalid phone number")]
    [MaxLength(20)]
    public string? Phone { get; set; }
}