using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.ClientPortal.Aliases.Requests;

public class GetPortalAliasCandidateRequest
{
    [FromQuery]
    public Ulid? PreviousEpithetId { get; set; }

    [FromQuery]
    public AliasNounKind? PreviousNounKind { get; set; }

    [FromQuery]
    public Ulid? PreviousNounId { get; set; }
}
