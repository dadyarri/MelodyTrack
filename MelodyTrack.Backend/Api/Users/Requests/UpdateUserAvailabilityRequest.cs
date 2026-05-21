using FastEndpoints;

namespace MelodyTrack.Backend.Api.Users.Requests;

public class UpdateUserAvailabilityRequest
{
    [BindFrom("id")]
    public Ulid Id { get; set; }
    public required List<UserWorkingHoursDayItem> WorkingHours { get; set; }
    public required List<UserVacationItem> Vacations { get; set; }
}

public class UserWorkingHoursDayItem
{
    public required string DayOfWeek { get; set; }
    public required bool IsWorkingDay { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
}

public class UserVacationItem
{
    public required DateOnly StartDate { get; set; }
    public required DateOnly EndDate { get; set; }
}
