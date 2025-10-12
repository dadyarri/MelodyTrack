namespace Backend.Api.Payments.Models;

/// <summary>
///     Запрос на создание платежа
/// </summary>
public class CreatePaymentRequest
{
    /// <summary>
    ///     Идентификатор клиента
    /// </summary>
    public required long ClientId { get; set; }

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
    public required string Description { get; set; } = string.Empty;
}