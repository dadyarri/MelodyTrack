using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.VacationRequests.Requests;

public sealed class CreateVacationRequest : IValidatableRequest
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? Message { get; set; }
}
