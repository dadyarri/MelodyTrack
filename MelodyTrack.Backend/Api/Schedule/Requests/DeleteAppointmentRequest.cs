using FastEndpoints;

namespace MelodyTrack.Backend.Api.Schedule.Requests;

public class DeleteAppointmentRequest
{
    [BindFrom("id")]
    public Ulid Id { get; set; }

    [BindFrom("scope")]
    public string? Scope { get; set; }
}
