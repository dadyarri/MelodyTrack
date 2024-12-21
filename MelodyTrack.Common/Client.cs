using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Common;

public class Client: BaseModel
{
    [MaxLength(100)] public required string FirstName { get; set; } = string.Empty;

    [MaxLength(100)] public required string LastName { get; set; } = string.Empty;

    [MaxLength(100)] public string? Patronymic { get; set; }

    public required ClientContact? Contacts { get; set; }

    public override string ToString()
    {
        return $"{FirstName} {LastName} {Contacts}";
    }
}