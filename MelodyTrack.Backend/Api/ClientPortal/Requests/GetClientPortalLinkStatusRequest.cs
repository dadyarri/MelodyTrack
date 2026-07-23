using FastEndpoints;

namespace MelodyTrack.Backend.Api.ClientPortal.Requests;

public class GetClientPortalLinkStatusRequest
{
    [BindFrom("token")]
    public required string Token { get; set; }
}
