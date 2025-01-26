namespace Backend.Api.Auth.Models;

public class LoginResponse
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string AccessToken { get; set; }
    public DateTime ExpireAt { get; set; }
}