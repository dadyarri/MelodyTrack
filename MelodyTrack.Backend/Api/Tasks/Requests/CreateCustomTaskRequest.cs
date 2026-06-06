namespace MelodyTrack.Backend.Api.Tasks.Requests;

public class CreateCustomTaskRequest
{
    public Ulid? ClientId { get; set; }
    public string? RecipientName { get; set; }
    public string? Phone { get; set; }
    public string? Telegram { get; set; }
    public string? Vk { get; set; }
    public required string Title { get; set; }
    public required string MessageText { get; set; }
    public required DateTime DueAtUtc { get; set; }
}
