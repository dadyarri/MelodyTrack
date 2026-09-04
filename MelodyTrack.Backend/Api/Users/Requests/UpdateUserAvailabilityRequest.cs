using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.Users.Requests;

public class UpdateUserAvailabilityRequest : IValidatableRequest
{
    [JsonIgnore]
    public Ulid Id { get; set; }
    public Ulid? ExpectedActivityId { get; set; }
    public required List<UserWorkingHoursDayItem> WorkingHours { get; set; }
    public required List<UserVacationItem> Vacations { get; set; }
    public bool CancelConflictingAppointments { get; set; }
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
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
}
