using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;

namespace MelodyTrack.Backend.Api.Common.Requests;

public class PaginatedRequest
{
    [FromQuery(Name = "page")]
    [DefaultValue(1)]
    public int? Page { get; set; }

    [FromQuery(Name = "page_size")]
    [DefaultValue(10)]
    public int? PageSize { get; set; }

    internal int EffectivePage => Page ?? 1;
    internal int EffectivePageSize => PageSize ?? 10;
}
