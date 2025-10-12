using System.ComponentModel.DataAnnotations;
using JetBrains.Annotations;

namespace Backend.Api.Expenses.Models;

/// <summary>
///     Тело запроса на создание расхода
/// </summary>
[UsedImplicitly]
public class CreateExpenseRequest
{
    /// <summary>
    ///     Описание
    /// </summary>
    [MaxLength(200)]
    public required string Description { get; set; }

    /// <summary>
    ///     Сумма
    /// </summary>
    public required decimal Amount { get; set; }
}