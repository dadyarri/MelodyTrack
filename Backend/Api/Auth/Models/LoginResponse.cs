namespace Backend.Api.Auth.Models;

/// <summary>
/// Тело ответа на успешный запрос на вход
/// </summary>
public class LoginResponse
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
    /// Токен доступа
    /// </summary>
    public required string AccessToken { get; set; }

    /// <summary>
    /// Токен истекает
    /// </summary>
    public DateTime ExpireAt { get; set; }
}