using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Migrator.OldData.Entities;

public class OldExpense : OldBaseModel
{
    /// <summary>
    ///     Описание
    /// </summary>
    [MaxLength(200)]
    public required string Description { get; set; } = string.Empty;

    /// <summary>
    ///     Сумма
    /// </summary>
    public required decimal Amount { get; set; }

    /// <summary>
    ///     Дата
    /// </summary>
    public required DateTime Date { get; set; } = DateTime.UtcNow;
}