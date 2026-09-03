using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.WorkingHoursRequests.Requests;

public sealed class CreateWorkingHoursRequest : IValidatableRequest
{
    public required List<WorkingHoursRequestDayInput> WorkingHours { get; set; }
    public string? Message { get; set; }
}

public sealed class WorkingHoursRequestDayInput
{
    public required string DayOfWeek { get; set; }
    public required bool IsWorkingDay { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
}
