using Backend.Data.Entities;

namespace Backend.Api.Clients.Models;

/// <summary>
///     Тело ответа, описывающего клиента с его балансом
/// </summary>
public class ClientWithBalanceResponse
{
    /// <summary>
    ///     Идентификатор
    /// </summary>
    public required long Id { get; set; }

    /// <summary>
    ///     Имя
    /// </summary>
    public required string FirstName { get; set; }

    /// <summary>
    ///     Фамилия
    /// </summary>
    public required string LastName { get; set; }

    /// <summary>
    ///     Отчество
    /// </summary>
    public string? Patronymic { get; set; }

    /// <summary>
    ///     Контакты
    /// </summary>
    public required ClientContact? Contacts { get; set; }

    /// <summary>
    ///     Баланс
    /// </summary>
    public required decimal Balance { get; set; }
}