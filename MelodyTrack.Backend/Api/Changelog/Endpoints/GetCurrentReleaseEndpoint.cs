using FastEndpoints;
using MelodyTrack.Backend.Api.Releases.Responses;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;

namespace MelodyTrack.Backend.Api.Releases.Endpoints;

public sealed class GetCurrentReleaseEndpoint(ReleaseChangelog changelog) : EndpointWithoutRequest<CurrentReleaseResponse>
{
    public override void Configure()
    {
        Get("/releases/current");
        AllowAnonymous();
        Options(builder => builder.RequireRateLimiting(ApiRateLimitPolicies.Releases));
        Description(builder => builder.Produces(StatusCodes.Status304NotModified));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (ApplyCaching(HttpContext, changelog.Etag))
        {
            await Send.ResultAsync(TypedResults.StatusCode(StatusCodes.Status304NotModified));
            return;
        }

        await Send.OkAsync(new CurrentReleaseResponse(changelog.Current.Version, changelog.Current.ResolvedCodename), ct);
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
