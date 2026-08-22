using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.Payments.Requests;

public class UpdatePaymentRequest : IValidatableRequest
{
    [JsonIgnore]
    public Ulid Id { get; set; }
    public Ulid? ExpectedActivityId { get; set; }
    public required Ulid ClientId { get; set; }
    public Ulid? ServiceId { get; set; }
    public required decimal Amount { get; set; }
    public required DateTime Date { get; set; }
    public string? Description { get; set; }
}
