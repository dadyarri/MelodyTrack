
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.Schedule.Requests;

public class DeleteAppointmentRequest
{
    [FromRoute]
    public Ulid Id { get; set; }

    [FromQuery(Name = "scope")]
    public string? Scope { get; set; }

    [FromQuery(Name = "expectedActivityId")]
    public Ulid? ExpectedActivityId { get; set; }
}
