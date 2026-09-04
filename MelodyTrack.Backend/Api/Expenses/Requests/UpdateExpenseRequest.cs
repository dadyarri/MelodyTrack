using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.Expenses.Requests;

public class UpdateExpenseRequest : IValidatableRequest
{
    [JsonIgnore]
    public Ulid Id { get; set; }
    public Ulid? ExpectedActivityId { get; set; }
    public required string Description { get; set; }
    public required decimal Amount { get; set; }
    public required DateTime Date { get; set; }
    public Ulid? CategoryId { get; set; }
}
