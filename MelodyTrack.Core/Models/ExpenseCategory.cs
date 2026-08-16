using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Backend.Data.Models;

public class ExpenseCategory : BaseModel
{
    [MaxLength(200)]
    public required string Name { get; set; }
}
