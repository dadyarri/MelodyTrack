using System.ComponentModel.DataAnnotations;
using JetBrains.Annotations;

namespace Backend.Api.Clients.Models;

/// <summary>
/// Запрос на обновление клиента
/// </summary>
[UsedImplicitly]
public class UpdateClientRequest
{
    /// <summary>
    /// Имя
    /// </summary>
    [MaxLength(100)] public required string FirstName { get; set; }

    /// <summary>
    /// Фамилия
    /// </summary>
    [MaxLength(100)] public required string LastName { get; set; }

    /// <summary>
    /// Отчество
    /// </summary>
    [MaxLength(100)] public string? Patronymic { get; set; }

    /// <summary>
    /// Ссылка на ВК
    /// </summary>
    [Url] public string? Vk { get; set; }

    /// <summary>
    /// Ссылка на Telegram
    /// </summary>
    [Url] public string? Telegram { get; set; }

    /// <summary>
    /// Номер телефона
    /// </summary>
    [RegularExpression(@"^[\+]?[(]?[0-9]{3}[)]?[-\s\.]?[0-9]{3}[-\s\.]?[0-9]{4,6}$",
        ErrorMessage = "Invalid phone number")]
    public string? Phone { get; set; }
}