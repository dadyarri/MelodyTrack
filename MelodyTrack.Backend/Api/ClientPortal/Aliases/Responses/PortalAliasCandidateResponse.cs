namespace MelodyTrack.Backend.Api.ClientPortal.Aliases.Responses;

public class PortalAliasCandidateResponse
{
    public required Ulid EpithetId { get; set; }
    public required AliasNounKind NounKind { get; set; }
    public required Ulid NounId { get; set; }
    public required string Value { get; set; }
    public required string CandidateToken { get; set; }
}
