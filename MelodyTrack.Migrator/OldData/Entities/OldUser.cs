using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Migrator.OldData.Entities;

public class OldUser : OldBaseModel
{
    /// <summary>
    ///     Имя пользователя
    /// </summary>
    [MaxLength(30)]
    public required string Username { get; set; }

    /// <summary>
    ///     Имя
    /// </summary>
    [MaxLength(30)]
    public required string FirstName { get; set; }

    /// <summary>
    ///     Фамилия
    /// </summary>
    [MaxLength(30)]
    public required string LastName { get; set; }

    /// <summary>
    ///     Хеш пароля
    /// </summary>
    public required byte[] PasswordHash { get; set; }

    /// <summary>
    ///     Соль пароля
    /// </summary>
    public required byte[] PasswordSalt { get; set; }
}