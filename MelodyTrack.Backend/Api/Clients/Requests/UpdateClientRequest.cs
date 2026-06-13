using FastEndpoints;

namespace MelodyTrack.Backend.Api.Clients.Requests;

public class UpdateClientRequest
{
    [BindFrom("id")]
    public Ulid Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Patronymic { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Email { get; set; }
    public string? Vk { get; set; }
    public string? Telegram { get; set; }
    public string? Phone { get; set; }
    public Ulid? SourceId { get; set; }
    public Ulid? ExpectedActivityId { get; set; }
}
