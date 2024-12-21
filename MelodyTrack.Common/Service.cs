namespace MelodyTrack.Common;

using System.ComponentModel.DataAnnotations;

public class Service : BaseModel
{
    [MaxLength(200)] public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;

    [EmailAddress] public required string Provider { get; set; }
}