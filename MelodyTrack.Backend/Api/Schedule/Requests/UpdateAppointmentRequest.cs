using FastEndpoints;

namespace MelodyTrack.Backend.Api.Schedule.Requests;

public class UpdateAppointmentRequest
{
    [BindFrom("id")]
    public Ulid Id { get; set; }
    public Ulid? ClientId { get; set; }
    public Ulid? ServiceId { get; set; }
    public Ulid? ProviderId { get; set; }
    public Ulid? RecurrenceTypeId { get; set; }
    public DateTime? StartDate { get; set; }
    public string? Timezone { get; set; }
    public string? Status { get; set; }
    public int? RecurrencePattern { get; set; }
    public Ulid? ExpectedActivityId { get; set; }
}
