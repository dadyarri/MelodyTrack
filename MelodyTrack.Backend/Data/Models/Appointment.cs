using MelodyTrack.Backend.Data.Enums;
using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Backend.Data.Models;

public class Appointment : BaseModel
{
    public Ulid? CourseThemeId { get; set; }
    [MaxLength(4000)]
    public string? LessonNotes { get; set; }
    public required Client Client { get; set; }
    public required Service Service { get; set; }
    public User? Provider { get; set; }
    public CourseTheme? CourseTheme { get; set; }
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
    public required AppointmentStatus Status { get; set; }
    public required bool IsDeleted { get; set; }
    public AppointmentRecurrenceRule? RecurringRule { get; set; }
}
