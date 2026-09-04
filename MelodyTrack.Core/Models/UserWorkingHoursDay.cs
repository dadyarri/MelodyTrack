namespace MelodyTrack.Backend.Data.Models;

public class UserWorkingHoursDay : BaseModel
{
    public required Ulid UserId { get; set; }
    public required User User { get; set; }
    public required DayOfWeek DayOfWeek { get; set; }
    public required bool IsWorkingDay { get; set; }
    public required int StartMinuteOfDay { get; set; }
    public required int EndMinuteOfDay { get; set; }
}
