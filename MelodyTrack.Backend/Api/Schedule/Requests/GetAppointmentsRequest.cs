namespace MelodyTrack.Backend.Api.Schedule.Requests;

public class GetAppointmentsRequest: BaseGetAppointmentsRequest
{
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
}