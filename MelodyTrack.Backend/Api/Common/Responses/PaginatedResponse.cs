using MelodyTrack.Backend.Api.Common.Requests;

namespace MelodyTrack.Backend.Api.Common.Responses;

public class PaginatedResponse
{
    public static PaginatedResponse<TData> Create<TData>(List<TData> data, long totalCount, PaginatedRequest request)
    {
        var skipped = request.PageSize * (request.Page - 1);
        return new PaginatedResponse<TData>
        {
            Data = data,
            Info = new PagedInfo
            {
                Page = request.Page,
                PageSize = request.PageSize,
                Total = totalCount,
                HasNextPage = skipped + request.PageSize < totalCount,
                HasPrevPage = request.Page > 1
            }
        };
    }
}

public class PaginatedResponse<T> : PaginatedResponse
{
    public required List<T> Data { get; set; }
    public required PagedInfo Info { get; set; }
}

public class PagedInfo
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public long Total { get; set; }
    public bool HasPrevPage { get; set; }
    public bool HasNextPage { get; set; }
}