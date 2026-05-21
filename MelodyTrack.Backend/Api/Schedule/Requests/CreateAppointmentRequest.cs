namespace MelodyTrack.Backend.Api.Schedule.Requests;

public class CreateAppointmentRequest
{
    public Ulid ClientId { get; set; }
    public Ulid ServiceId { get; set; }
    public Ulid? ProviderId { get; set; }
    public Ulid? RecurrenceTypeId { get; set; }
    public DateTime StartDate { get; set; }
    public required string Timezone { get; set; }
    public DateTime? PatternEndDate { get; set; }
    public int? RecurrencePattern { get; set; }
}
