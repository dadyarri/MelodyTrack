namespace MelodyTrack.Backend.Api.Schedule.Responses;

public class GetMiniScheduleResponse
{
    public Dictionary<string, List<AppointmentDto>> Appointments { get; set; }
}