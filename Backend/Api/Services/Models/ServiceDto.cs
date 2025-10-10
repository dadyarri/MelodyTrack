namespace Backend.Api.Services.Models;

/// <summary>
/// Описание услуги
/// </summary>
public class ServiceDto
{
    /// <summary>
    /// Идентификатор
    /// </summary>
    public required long Id { get; set; }
    /// <summary>
    /// Название
    /// </summary>
    public required string Name { get; set; }
    /// <summary>
    /// Описание
    /// </summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>
    /// Текущая цена
    /// </summary>
    public decimal CurrentPrice { get; set; }
}