using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Migrator.OldData.Entities;

public class OldService : OldBaseModel
{
    /// <summary>
    ///     Название
    /// </summary>
    [MaxLength(200)]
    public required string Name { get; set; }

    /// <summary>
    ///     Описание
    /// </summary>
    [MaxLength(200)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    ///     Пользователь, оказывающий услугу
    /// </summary>
    public required OldUser Provider { get; set; }
}