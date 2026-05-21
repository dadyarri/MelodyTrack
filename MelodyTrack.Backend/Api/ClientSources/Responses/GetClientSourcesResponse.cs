using MelodyTrack.Backend.Api.Common.Responses;

namespace MelodyTrack.Backend.Api.ClientSources.Responses;

public class GetClientSourcesResponse
{
    public required List<ReferenceBookItemDto> Sources { get; set; }
}
