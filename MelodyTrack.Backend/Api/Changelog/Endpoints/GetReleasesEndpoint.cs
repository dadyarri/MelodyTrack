using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Releases.Requests;
using MelodyTrack.Backend.Api.Releases.Responses;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MelodyTrack.Backend.Api.Releases.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/releases")]
public sealed class GetReleasesEndpoint
{
    [AllowAnonymous]
    [EnableRateLimiting(ApiRateLimitPolicies.Releases)]
    [ProducesResponseType<ReleasesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public static Results<Ok<ReleasesResponse>, StatusCodeHttpResult> HandleAsync(
        [AsParameters] GetReleasesRequest req,
        ReleaseChangelog changelog,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (GetCurrentReleaseEndpoint.ApplyCaching(httpContext, changelog.Etag))
        {
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        }

        var releases = changelog.Releases
            .Skip((req.EffectivePage - 1) * req.EffectivePageSize)
            .Take(req.EffectivePageSize)
            .Select(release => new ReleaseResponse(
                release.Version,
                release.ResolvedCodename,
                release.Date,
                release.Changes,
                release.ParentVersion))
            .ToArray();
        var totalCount = changelog.Releases.Count;
        return TypedResults.Ok(new ReleasesResponse(
            changelog.Current.Version,
            releases,
            req.EffectivePage,
            req.EffectivePageSize,
            totalCount,
            (int)Math.Ceiling(totalCount / (double)req.EffectivePageSize),
            req.EffectivePage * req.EffectivePageSize < totalCount));
    }
}
