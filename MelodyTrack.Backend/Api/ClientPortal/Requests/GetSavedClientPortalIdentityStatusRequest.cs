using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.ClientPortal.Requests;

public class GetSavedClientPortalIdentityStatusRequest : IValidatableRequest
{
    [FromQuery]
    public required string Reference { get; set; }
}
