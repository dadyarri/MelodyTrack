namespace MelodyTrack.Backend.Api.Common.Responses;

public class PaginatedResponse<T>
{
    public required List<T> Data { get; set; }
    public required PagedInfo Info { get; set; }

    public static PaginatedResponse<T> Create(List<T> data, long count, int page, int pageSize, long skipped)
    {
        return new PaginatedResponse<T>
        {
            Data = data,
            Info = new PagedInfo
            {
                Page = page,
                PageSize = pageSize,
                Total = count,
                HasNextPage = skipped + pageSize < count,
                HasPrevPage = page > 1
            }
        };
    }
}

public class PagedInfo
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public long Total { get; set; }
    public bool HasPrevPage { get; set; }
    public bool HasNextPage { get; set; }
}