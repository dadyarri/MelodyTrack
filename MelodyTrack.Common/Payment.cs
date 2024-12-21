namespace MelodyTrack.Common;

using System.ComponentModel.DataAnnotations;

public class Payment : BaseModel
{
    public required Client Client { get; set; }

    [Range(0, (double)decimal.MaxValue)] public required decimal Amount { get; set; }

    public required DateTime Date { get; set; } = DateTime.UtcNow;

    public required string Description { get; set; } = string.Empty;
}