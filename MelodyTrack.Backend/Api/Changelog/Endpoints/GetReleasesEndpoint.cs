using FastEndpoints;
using MelodyTrack.Backend.Api.Releases.Requests;
using MelodyTrack.Backend.Api.Releases.Responses;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;

namespace MelodyTrack.Backend.Api.Releases.Endpoints;

public sealed class GetReleasesEndpoint(ReleaseChangelog changelog) : Endpoint<GetReleasesRequest, ReleasesResponse>
{
    public override void Configure()
    {
        Get("/releases");
        AllowAnonymous();
        Options(builder => builder.RequireRateLimiting(ApiRateLimitPolicies.Releases));
        Validator<GetReleasesValidator>();
        Description(builder => builder.Produces(StatusCodes.Status304NotModified));
    }

    public override async Task HandleAsync(GetReleasesRequest req, CancellationToken ct)
    {
        if (GetCurrentReleaseEndpoint.ApplyCaching(HttpContext, changelog.Etag))
        {
            await Send.ResultAsync(TypedResults.StatusCode(StatusCodes.Status304NotModified));
            return;
        }

        var releases = changelog.Releases
            .Skip((req.Page - 1) * req.PageSize)
            .Take(req.PageSize)
            .Select(release => new ReleaseResponse(
                release.Version,
                release.ResolvedCodename,
                release.Date,
                release.Changes,
                release.ParentVersion))
            .ToArray();
        var totalCount = changelog.Releases.Count;
        await Send.OkAsync(new ReleasesResponse(
            changelog.Current.Version,
            releases,
            req.Page,
            req.PageSize,
            totalCount,
            (int)Math.Ceiling(totalCount / (double)req.PageSize),
            req.Page * req.PageSize < totalCount), ct);
    }
}
