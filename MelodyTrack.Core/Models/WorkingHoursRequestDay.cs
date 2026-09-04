namespace MelodyTrack.Backend.Data.Models;

public sealed class WorkingHoursRequestDay : BaseModel
{
    public required Ulid WorkingHoursRequestId { get; set; }
    public required WorkingHoursRequest WorkingHoursRequest { get; set; }
    public required DayOfWeek DayOfWeek { get; set; }
    public required bool IsWorkingDay { get; set; }
    public required int StartMinuteOfDay { get; set; }
    public required int EndMinuteOfDay { get; set; }
}
