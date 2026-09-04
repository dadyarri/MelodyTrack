using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.ClientPortal.Requests;

public class GetClientPortalLinkStatusRequest : IValidatableRequest
{
    [FromQuery(Name = "token")]
    public required string Token { get; set; }
}
