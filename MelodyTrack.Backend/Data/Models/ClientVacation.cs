using System.Text.Json.Serialization;

namespace MelodyTrack.Backend.Data.Models;

public class ClientVacation : BaseModel
{
    public required Ulid ClientId { get; set; }
    [JsonIgnore]
    public Client Client { get; set; } = null!;
    public required DateOnly StartDate { get; set; }
    public required DateOnly EndDate { get; set; }
}
