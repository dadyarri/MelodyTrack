namespace MelodyTrack.Backend.Api.ClientPortal.Responses;

public class GetClientPortalScheduleResponse
{
    public required List<ClientPortalAppointmentDto> Appointments { get; set; }
}
