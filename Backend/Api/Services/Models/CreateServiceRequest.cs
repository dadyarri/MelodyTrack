using System.ComponentModel.DataAnnotations;

namespace Backend.Api.Services.Models;

/// <summary>
/// Запрос на создание услуги
/// </summary>
public class CreateServiceRequest
{
    /// <summary>
    /// Название
    /// </summary>
    [MaxLength(200)] public required string Name { get; set; }

    /// <summary>
    /// Описание
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Цена
    /// </summary>
    public decimal Price { get; set; }
}