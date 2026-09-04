using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.ClientSources.Requests;

public class CreateClientSourceRequest : IValidatableRequest
{
    public required string Name { get; set; }
}
