namespace MelodyTrack.Common.Api.Expenses.Requests;

public class CreateExpenseRequest
{
    public required string Description { get; set; }
    public required decimal Amount { get; set; }
}