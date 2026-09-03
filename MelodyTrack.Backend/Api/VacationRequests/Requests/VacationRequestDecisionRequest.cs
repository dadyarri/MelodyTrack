using System.Text.Json.Serialization;
using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.VacationRequests.Requests;

public sealed class VacationRequestDecisionRequest : IValidatableRequest
{
    [JsonIgnore]
    public Ulid Id { get; set; }
    public int ExpectedVersion { get; set; }
    public string? Message { get; set; }
}
