using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Releases.Responses;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MelodyTrack.Backend.Api.Releases.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/releases/current")]
public sealed class GetCurrentReleaseEndpoint
{
    [AllowAnonymous]
    [EnableRateLimiting(ApiRateLimitPolicies.Releases)]
    [ProducesResponseType<CurrentReleaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public static Results<Ok<CurrentReleaseResponse>, StatusCodeHttpResult> HandleAsync(
        HttpContext context,
        ReleaseChangelog changelog,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ApplyCaching(context, changelog.Etag))
        {
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        }

        return TypedResults.Ok(new CurrentReleaseResponse(changelog.Current.Version, changelog.Current.ResolvedCodename));
    }

    internal static bool ApplyCaching(HttpContext context, string etag)
    {
        context.Response.Headers.CacheControl = "public, max-age=300, stale-if-error=86400";
        context.Response.Headers.ETag = etag;
        if (!context.Request.Headers.IfNoneMatch.Any(value => string.Equals(value, etag, StringComparison.Ordinal)))
        {
            return false;
        }

        return true;
    }
}
