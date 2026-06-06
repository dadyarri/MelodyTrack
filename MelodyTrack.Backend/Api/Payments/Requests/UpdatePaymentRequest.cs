using FastEndpoints;

namespace MelodyTrack.Backend.Api.Payments.Requests;

public class UpdatePaymentRequest
{
    [BindFrom("id")]
    public Ulid Id { get; set; }
    public Ulid? ExpectedActivityId { get; set; }
    public required Ulid ClientId { get; set; }
    public Ulid? ServiceId { get; set; }
    public required decimal Amount { get; set; }
    public required DateTime Date { get; set; }
    public string? Description { get; set; }
}
