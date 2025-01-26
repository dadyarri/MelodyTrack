namespace Backend.Api.Auth.Models;

public class RegisterRequest
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Username { get; set; }
    public required string Password { get; set; }
}