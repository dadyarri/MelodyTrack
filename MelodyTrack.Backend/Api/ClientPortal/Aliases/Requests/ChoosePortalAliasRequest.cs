using System.ComponentModel.DataAnnotations;
using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.ClientPortal.Aliases.Requests;

public class ChoosePortalAliasRequest : IValidatableRequest
{
    public Ulid EpithetId { get; set; }
    public AliasNounKind NounKind { get; set; }
    public Ulid NounId { get; set; }

    [Required]
    public string Value { get; set; } = string.Empty;

    [Required]
    public string CandidateToken { get; set; } = string.Empty;
}
