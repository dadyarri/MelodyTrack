using FastEndpoints;
using JetBrains.Annotations;

namespace Backend.Api.Base.Models;

[UsedImplicitly]
public class PaginationRequest
{
    [BindFrom("page")] public int Page { get; set; }
    [BindFrom("page_size")] public int PageSize { get; set; }
}