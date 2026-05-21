using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Clients.Responses;

public class LookupClientDto
{
    public required Ulid Id { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Patronymic { get; set; }
    public ClientHistoryContactsDto? Contacts { get; set; }
    public Ulid? SourceId { get; set; }
    public string? SourceName { get; set; }
}
