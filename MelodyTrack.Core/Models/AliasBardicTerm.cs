namespace MelodyTrack.Backend.Data.Models;

public class AliasBardicTerm : BaseModel
{
    public required string Value { get; set; }
    public bool IsEnabled { get; set; }
}
