using MelodyTrack.Backend.Data.Enums;

namespace MelodyTrack.Backend.Data.Models;

public sealed class SchedulingInterruption : BaseModel
{
    public Ulid? VacationId { get; set; }
    public Vacation? Vacation { get; set; }
    public VacationRequestSubjectType SubjectType { get; set; }
    public Ulid SubjectId { get; set; }
    public Ulid? AppointmentId { get; set; }
    public Appointment? Appointment { get; set; }
    public Ulid? RecurringRuleId { get; set; }
    public AppointmentRecurrenceRule? RecurringRule { get; set; }
    public long? RecurrenceOrdinal { get; set; }
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
    public required string Reason { get; set; }
    public DateTime? SupersededAtUtc { get; set; }
    public DateTime? RestoredAtUtc { get; set; }
}
