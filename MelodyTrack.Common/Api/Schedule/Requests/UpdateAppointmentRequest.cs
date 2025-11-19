using FastEndpoints;

namespace MelodyTrack.Common.Api.Schedule.Requests;

public class UpdateAppointmentRequest
{
    [BindFrom("id")]
    public Ulid Id { get; set; }
    public Ulid? ClientId { get; set; }
    public Ulid? ServiceId { get; set; }
    public Ulid? ProviderId { get; set; }
    public Ulid? RecurrenceTypeId { get; set; }
    public DateTime? StartDate { get; set; }
    public bool? IsCompleted { get; set; }
    public bool? IsCanceled { get; set; }
    public int? RecurrencePattern { get; set; }
}