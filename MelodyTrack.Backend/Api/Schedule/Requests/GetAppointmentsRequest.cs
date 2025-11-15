namespace MelodyTrack.Backend.Api.Schedule.Requests;

public class GetAppointmentsRequest
{
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
    public required string Timezone { get; set; }
}