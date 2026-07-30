using FastEndpoints;

namespace MelodyTrack.Backend.Api.Releases.Requests;

public sealed class GetReleasesRequest
{
    [BindFrom("page")]
    public int Page { get; set; } = 1;

    [BindFrom("page_size")]
    public int PageSize { get; set; } = 2;
}
