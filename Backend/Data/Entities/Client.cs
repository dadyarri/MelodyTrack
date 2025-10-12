using System.ComponentModel.DataAnnotations;

namespace Backend.Data.Entities;

/// <summary>
///     Клиент
/// </summary>
public class Client : BaseModel
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
    public required ClientContact? Contacts { get; set; }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{FirstName} {LastName} {Contacts}";
    }
}