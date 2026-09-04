using System.Text.Json.Serialization;

namespace MelodyTrack.Backend.Data.Models;

public class ClientVacation : BaseModel
{
    public required Ulid ClientId { get; set; }
    [JsonIgnore]
    public Client Client { get; set; } = null!;
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
}
