namespace MelodyTrack.Backend.Api.Tasks.Responses;

public class GetDueRecurringTasksResponse
{
    public required List<RecurringTaskDto> Tasks { get; set; }
}
