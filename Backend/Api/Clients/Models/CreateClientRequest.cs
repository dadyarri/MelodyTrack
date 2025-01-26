using System.ComponentModel.DataAnnotations;
using JetBrains.Annotations;

namespace Backend.Api.Clients.Models;

[UsedImplicitly]
public class CreateClientRequest
{
    [MaxLength(100)] public required string FirstName { get; set; }

    [MaxLength(100)] public required string LastName { get; set; }

    [MaxLength(100)] public string? Patronymic { get; set; }

    [Url] public string? Vk { get; set; }

    [Url] public string? Telegram { get; set; }

    [RegularExpression(@"^[\+]?[(]?[0-9]{3}[)]?[-\s\.]?[0-9]{3}[-\s\.]?[0-9]{4,6}$",
        ErrorMessage = "Invalid phone number")]
    public string? Phone { get; set; }
}