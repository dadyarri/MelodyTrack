using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.Auth.Requests;

public class GetInviteCodeInformationRequest : IValidatableRequest
{
    public required string InviteCode { get; set; }
}