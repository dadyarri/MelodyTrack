using System.ComponentModel;
using FastEndpoints;

namespace MelodyTrack.Common.Api.Common.Requests;

public class PaginatedRequest
{
    [BindFrom("page")]
    [DefaultValue(1)]
    public int Page { get; set; }

    [BindFrom("page_size")]
    [DefaultValue(10)]
    public int PageSize { get; set; }
}