using Backend.Data.Entities;

namespace Backend.Api.Clients.Models;

/// <summary>
/// Тело ответа на запрос существующего клиента
/// </summary>
public class GetClientResponse
{
    /// <summary>
    /// Имя
    /// </summary>
    public required string FirstName { get; set; }
    /// <summary>
    /// Фамилия
    /// </summary>
    public required string LastName { get; set; }
    /// <summary>
    /// Отчество
    /// </summary>
    public string? Patronymic { get; set; }
    /// <summary>
    /// Контакты
    /// </summary>
    public required ClientContact? Contacts { get; set; }
    /// <summary>
    /// Последние платежи
    /// </summary>
    public required List<Payment> LatestPayments { get; set; } = [];
}