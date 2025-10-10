namespace Backend.Api.Auth.Models;

/// <summary>
/// Тело запроса на вход существующего пользователя
/// </summary>
public class LoginRequest
{
    /// <summary>
    /// Имя пользователя
    /// </summary>
    public required string Username { get; set; }
    /// <summary>
    /// Пароль
    /// </summary>
    public required string Password { get; set; }
}