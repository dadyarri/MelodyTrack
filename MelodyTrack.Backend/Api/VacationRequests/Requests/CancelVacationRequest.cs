using System.Text.Json.Serialization;
using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.VacationRequests.Requests;

public sealed class CancelVacationRequest : IValidatableRequest
{
    [JsonIgnore]
    public Ulid Id { get; set; }
    public int ExpectedVersion { get; set; }
}
