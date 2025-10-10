namespace Backend.Data.Entities;

public class Payment : BaseModel
{
    public required Client Client { get; set; }

    public required decimal Amount { get; set; }

    public required DateTime Date { get; set; } = DateTime.UtcNow;

    public required string Description { get; set; } = string.Empty;
}