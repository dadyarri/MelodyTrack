namespace MelodyTrack.Backend.Data.Models;

public class Appointment : BaseModel
{
    public required Client Client { get; set; }
    public required Service Service { get; set; }
    public User? Provider { get; set; }
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
    public required bool IsCompleted { get; set; }
    public required bool IsCanceled { get; set; }
    public required bool IsDeleted { get; set; }
    public AppointmentRecurrenceRule? RecurringRule { get; set; }
}
