using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.VacationRequests.Requests;

public sealed class CreateVacationRequest : IValidatableRequest
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? Message { get; set; }
}
