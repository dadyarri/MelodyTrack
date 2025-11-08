using MelodyTrack.Backend.Data.Enums;

namespace MelodyTrack.Backend.Data.Models;

public class RecurrenceType : BaseModel
{
    public AppointmentRecurrenceType Type { get; set; }
    public required string DisplayName { get; set; }
}