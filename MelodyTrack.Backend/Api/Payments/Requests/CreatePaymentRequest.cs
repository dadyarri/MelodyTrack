using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.Payments.Requests;

public class CreatePaymentRequest : IValidatableRequest
{
    public required Ulid ClientId { get; set; }
    public Ulid? ServiceId { get; set; }
    public required decimal Amount { get; set; }
    public required DateTime Date { get; set; }
    public string? Description { get; set; }
}
