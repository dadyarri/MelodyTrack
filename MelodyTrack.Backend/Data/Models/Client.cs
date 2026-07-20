using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Backend.Data.Models;

public class Client : BaseModel
{
    [MaxLength(100)]
    public required string FirstName { get; set; }

    [MaxLength(100)]
    public required string LastName { get; set; }

    [MaxLength(100)]
    public string? Patronymic { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public Ulid? SourceId { get; set; }

    public ClientSource? Source { get; set; }

    public required DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public required ClientContacts Contacts { get; set; } = new();

    public List<ClientVacation> Vacations { get; set; } = [];
}
