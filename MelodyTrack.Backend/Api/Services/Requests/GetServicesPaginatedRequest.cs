using MelodyTrack.Backend.Api.Common.Requests;

namespace MelodyTrack.Backend.Api.Services.Requests;

public class GetServicesPaginatedRequest : PaginatedRequest
{
    public string? Name { get; set; }
}