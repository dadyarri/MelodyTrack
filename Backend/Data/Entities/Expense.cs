using System.ComponentModel.DataAnnotations;

namespace Backend.Data.Entities;

/// <summary>
/// Расход
/// </summary>
public class Expense : BaseModel
{
    /// <summary>
    /// Описание
    /// </summary>
    [MaxLength(200)] public required string Description { get; set; } = string.Empty;

    /// <summary>
    /// Сумма
    /// </summary>
    public required decimal Amount { get; set; }

    /// <summary>
    /// Дата
    /// </summary>
    public required DateTime Date { get; set; } = DateTime.UtcNow;
}