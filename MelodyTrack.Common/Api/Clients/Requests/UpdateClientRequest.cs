using FastEndpoints;

namespace MelodyTrack.Common.Api.Clients.Requests;

public class UpdateClientRequest
{
    [BindFrom("id")]
    public Ulid Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Patronymic { get; set; }
    public string? Vk { get; set; }
    public string? Telegram { get; set; }
    public string? Phone { get; set; }
}