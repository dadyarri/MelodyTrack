namespace MelodyTrack.Backend.Data.Models;

public class AliasCreature : BaseModel
{
    public required string Value { get; set; }
    public bool IsEnabled { get; set; }
}
