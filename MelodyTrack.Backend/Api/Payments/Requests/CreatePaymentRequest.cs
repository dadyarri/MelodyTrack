namespace MelodyTrack.Backend.Api.Payments.Requests;

public class CreatePaymentRequest
{
    public required Ulid ClientId { get; set; }
    public Ulid? ServiceId { get; set; }
    public required decimal Amount { get; set; }
    public required DateTime Date { get; set; }
    public required string Description { get; set; }
}