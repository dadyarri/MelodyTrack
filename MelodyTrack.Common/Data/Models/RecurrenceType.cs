using MelodyTrack.Common.Data.Enums;

namespace MelodyTrack.Common.Data.Models;

public class RecurrenceType : BaseModel
{
    public AppointmentRecurrenceType Type { get; set; }
    public required string DisplayName { get; set; }
}