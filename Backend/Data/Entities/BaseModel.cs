using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Data.Entities;

/// <summary>
///     Базовая модель
/// </summary>
public class BaseModel
{
    /// <summary>
    ///     Идентификатор
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }
}