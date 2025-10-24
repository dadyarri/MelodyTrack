using FastEndpoints;

namespace MelodyTrack.Backend.Api.Common.Requests;

public class PaginatedRequest
{
    [BindFrom("page")] public int Page { get; set; }

    [BindFrom("page_size")] public int PageSize { get; set; }
}