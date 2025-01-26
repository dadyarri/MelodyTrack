using System.ComponentModel.DataAnnotations;

namespace Backend.Data.Entities;

public class User : BaseModel
{
    [MaxLength(30)] public required string Username { get; set; }
    [MaxLength(30)] public required string FirstName { get; set; }
    [MaxLength(30)] public required string LastName { get; set; }
    public required byte[] PasswordHash { get; set; }
    public required byte[] PasswordSalt { get; set; }
}