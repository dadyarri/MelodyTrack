using System.ComponentModel.DataAnnotations;

namespace Backend.Data.Entities;

/// <summary>
/// Платёж
/// </summary>
public class Payment : BaseModel
{
    /// <summary>
    /// Клиент
    /// </summary>
    public required Client Client { get; set; }

    /// <summary>
    /// Сумма
    /// </summary>
    public required decimal Amount { get; set; }

    /// <summary>
    /// Дата
    /// </summary>
    public required DateTime Date { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Описание
    /// </summary>
    [MaxLength(200)]
    public required string Description { get; set; } = string.Empty;
}