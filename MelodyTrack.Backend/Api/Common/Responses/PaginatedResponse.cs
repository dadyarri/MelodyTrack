using MelodyTrack.Backend.Api.Common.Requests;

namespace MelodyTrack.Backend.Api.Common.Responses;

public class PaginatedResponse
{
    public static PaginatedResponse<TData> Create<TData>(List<TData> items, long totalCount, PaginatedRequest request)
    {
        var skipped = request.EffectivePageSize * (request.EffectivePage - 1);
        return new PaginatedResponse<TData>
        {
            Items = items,
            Page = new PageMetadata
            {
                Page = request.EffectivePage,
                PageSize = request.EffectivePageSize,
                Total = totalCount,
                HasNextPage = skipped + request.EffectivePageSize < totalCount,
                HasPrevPage = request.EffectivePage > 1
            }
        };
    }
}

public class PaginatedResponse<T> : PaginatedResponse
{
    public required List<T> Items { get; set; }
    public required PageMetadata Page { get; set; }
}

public class PageMetadata
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public long Total { get; set; }
    public bool HasPrevPage { get; set; }
    public bool HasNextPage { get; set; }
}
