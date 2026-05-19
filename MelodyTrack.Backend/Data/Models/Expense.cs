using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Backend.Data.Models;

public class Expense : BaseModel
{
    [MaxLength(200)]
    public required string Description { get; set; } = string.Empty;

    public required decimal Amount { get; set; }

    public required DateTime Date { get; set; } = DateTime.UtcNow;

    public ExpenseCategory? Category { get; set; }
}