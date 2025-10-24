namespace MelodyTrack.Backend.Data.Models;

public class ClientContacts: BaseModel
{
    public string? Telegram { get; set; }
    public string? Vk { get; set; }
    public string? Phone { get; set; }
}