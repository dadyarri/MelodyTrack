namespace MelodyTrack.Backend.Api.Expenses.Responses;

public class ExpenseDto
{
    public required Ulid Id { get; set; }
    public required string Description { get; set; }
    public required decimal Amount { get; set; }
    public required DateTime Date { get; set; }
    public Ulid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
}
