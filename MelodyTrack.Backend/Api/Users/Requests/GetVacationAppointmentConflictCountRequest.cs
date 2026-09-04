using MelodyTrack.Backend.Validation;
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.Users.Requests;

public sealed class GetVacationAppointmentConflictCountRequest : IValidatableRequest
{
    [FromRoute]
    public Ulid Id { get; set; }

    [FromQuery]
    public DateTime StartDate { get; set; }

    [FromQuery]
    public DateTime EndDate { get; set; }
}
