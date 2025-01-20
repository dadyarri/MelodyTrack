using System.ComponentModel.DataAnnotations;

namespace Backend.Data.Entities;

public class Expense : BaseModel
{
    [MaxLength(200)] public required string Description { get; set; } = string.Empty;

    [Range(0, (double)decimal.MaxValue)] public required decimal Amount { get; set; }

    public required DateTime Date { get; set; } = DateTime.UtcNow;
}