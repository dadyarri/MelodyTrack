using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.ClientPortal.Responses;

public class ClientPortalAppointmentDto
{
    public required Ulid Id { get; set; }
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
    public required string Status { get; set; }

    public static ClientPortalAppointmentDto FromModel(Appointment appointment)
    {
        return new ClientPortalAppointmentDto
        {
            Id = appointment.Id,
            StartDate = appointment.StartDate,
            EndDate = appointment.EndDate,
            Status = appointment.Status.ToApiKey()
        };
    }
}