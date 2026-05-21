namespace MelodyTrack.Backend.Api.Users.Responses;

public class UserAvailabilityResponse
{
    public required Ulid UserId { get; set; }
    public required List<UserWorkingHoursDayDto> WorkingHours { get; set; }
    public required List<UserVacationDto> Vacations { get; set; }
}

public class UserWorkingHoursDayDto
{
    public required string DayOfWeek { get; set; }
    public required bool IsWorkingDay { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
}

public class UserVacationDto
{
    public required Ulid Id { get; set; }
    public required DateOnly StartDate { get; set; }
    public required DateOnly EndDate { get; set; }
}
