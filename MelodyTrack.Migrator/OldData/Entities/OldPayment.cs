using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Migrator.OldData.Entities;

public class OldPayment : OldBaseModel
{
    /// <summary>
    ///     Клиент
    /// </summary>
    public required OldClient Client { get; set; }

    /// <summary>
    ///     Сумма
    /// </summary>
    public required decimal Amount { get; set; }

    /// <summary>
    ///     Дата
    /// </summary>
    public required DateTime Date { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///     Описание
    /// </summary>
    [MaxLength(200)]
    public required string Description { get; set; } = string.Empty;

    /// <summary>
    ///     Услуга
    /// </summary>
    public OldService? Service { get; set; }
}