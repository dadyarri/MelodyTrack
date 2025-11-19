using MelodyTrack.Common.Api.Common.Requests;

namespace MelodyTrack.Common.Api.Services.Requests;

public class GetServicesPaginatedRequest : PaginatedRequest
{
    public string? Name { get; set; }
}