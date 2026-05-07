using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Backend.Data.Models;

public class Payment : BaseModel
{
    public required Client Client { get; set; }
    public required decimal Amount { get; set; }
    public required DateTime Date { get; set; }

    [MaxLength(200)]
    public required string Description { get; set; }

    public Service? Service { get; set; }
}