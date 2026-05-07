namespace MelodyTrack.Backend.Api.Schedule.Responses;

public class GetAppointmentsResponse
{
    public required List<AppointmentDto> Appointments { get; set; }
}