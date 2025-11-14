using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Migrator.OldData.Entities;

public class OldClient : OldBaseModel
{
    /// <summary>
    ///     Имя
    /// </summary>
    [MaxLength(100)]
    public required string FirstName { get; set; } = string.Empty;

    /// <summary>
    ///     Фамилия
    /// </summary>
    [MaxLength(100)]
    public required string LastName { get; set; } = string.Empty;

    /// <summary>
    ///     Отчество
    /// </summary>
    [MaxLength(100)]
    public string? Patronymic { get; set; }

    /// <summary>
    ///     Контакты
    /// </summary>
    public required OldClientContact? Contacts { get; set; }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{FirstName} {LastName} {Contacts}";
    }
}