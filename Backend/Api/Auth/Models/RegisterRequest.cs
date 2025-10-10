namespace Backend.Api.Auth.Models;

/// <summary>
/// Тело запроса на регистрацию нового пользователя
/// </summary>
public class RegisterRequest
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
    /// Имя пользователя
    /// </summary>
    public required string Username { get; set; }

    /// <summary>
    /// Пароль
    /// </summary>
    public required string Password { get; set; }
}