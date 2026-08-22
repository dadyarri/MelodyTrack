using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.Releases.Requests;

public sealed class GetReleasesRequest : IValidatableRequest
{
    [FromQuery(Name = "page")]
    public int? Page { get; set; }

    [FromQuery(Name = "page_size")]
    public int? PageSize { get; set; }

    internal int EffectivePage => Page ?? 1;
    internal int EffectivePageSize => PageSize ?? 2;
}
