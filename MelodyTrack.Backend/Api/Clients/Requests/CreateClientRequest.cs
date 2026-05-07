namespace MelodyTrack.Backend.Api.Clients.Requests;

public class CreateClientRequest
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Patronymic { get; set; }
    public string? Telegram { get; set; }
    public string? Vk { get; set; }
    public string? Phone { get; set; }
}
