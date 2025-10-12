using System.ComponentModel.DataAnnotations;

namespace Backend.Data.Entities;

/// <summary>
///     Услуга
/// </summary>
public class Service : BaseModel
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
    public required User Provider { get; set; }
}