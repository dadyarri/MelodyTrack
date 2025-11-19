using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Common.Data.Models;

public class Client : BaseModel
{
    [MaxLength(100)]
    public required string FirstName { get; set; }

    [MaxLength(100)]
    public required string LastName { get; set; }

    [MaxLength(100)]
    public string? Patronymic { get; set; }

    public required ClientContacts Contacts { get; set; } = new();
}