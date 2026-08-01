using FastEndpoints;

namespace MelodyTrack.Backend.Api.ClientPortal.Requests;

public class GetSavedClientPortalIdentityStatusRequest
{
    [QueryParam]
    public required string Reference { get; set; }
}
