namespace MelodyTrack.Backend.Data.Models;

public class ServicePrice : BaseModel
{
    public required Service Service { get; set; }
    public required decimal Price { get; set; }
    public required DateTime EffectiveDate { get; set; }
}