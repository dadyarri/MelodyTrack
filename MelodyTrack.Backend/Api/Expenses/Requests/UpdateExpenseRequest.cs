using FastEndpoints;

namespace MelodyTrack.Backend.Api.Expenses.Requests;

public class UpdateExpenseRequest
{
    [BindFrom("id")]
    public Ulid Id { get; set; }
    public Ulid? ExpectedActivityId { get; set; }
    public required string Description { get; set; }
    public required decimal Amount { get; set; }
    public required DateTime Date { get; set; }
    public Ulid? CategoryId { get; set; }
}
