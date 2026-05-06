namespace MelodyTrack.Backend.Api.Schedule.Responses;

public class GetMiniScheduleResponse
{
    public required Dictionary<string, List<AppointmentDto>> Appointments { get; set; }
}
