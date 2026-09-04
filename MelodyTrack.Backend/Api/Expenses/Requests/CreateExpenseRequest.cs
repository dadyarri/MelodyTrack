using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.Expenses.Requests;

public class CreateExpenseRequest : IValidatableRequest
{
    public required string Description { get; set; }
    public required decimal Amount { get; set; }
    public required DateTime Date { get; set; }
    public Ulid? CategoryId { get; set; }
}
