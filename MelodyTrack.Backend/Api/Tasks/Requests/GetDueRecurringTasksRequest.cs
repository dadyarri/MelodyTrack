namespace MelodyTrack.Backend.Api.Tasks.Requests;

public class GetDueRecurringTasksRequest
{
    public required string Timezone { get; set; }
    public string? Type { get; set; }
}
