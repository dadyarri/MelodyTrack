using System.Threading.RateLimiting;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Rewrite.Api;

public static class ReleaseEndpoints
{
    private const string RatePolicy = "public-releases";

    public static IServiceCollection AddReleaseChangelog(this IServiceCollection services)
    {
        services.AddSingleton(ReleaseChangelog.LoadEmbedded());
        services.AddRateLimiter(options => options.AddPolicy(RatePolicy, context =>
            RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new()
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            })));
        return services;
    }

    public static IEndpointRouteBuilder MapReleaseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/releases").AllowAnonymous().RequireRateLimiting(RatePolicy);
        group.MapGet("/current", GetCurrent).WithName("GetCurrentRelease")
            .Produces(304).ProducesProblem(429);
        group.MapGet("", GetHistory).WithName("GetReleaseHistory")
            .Produces(304).ProducesProblem(400).ProducesProblem(429);
        return endpoints;
    }

    private static Results<Ok<CurrentReleaseResponse>, StatusCodeHttpResult> GetCurrent(HttpContext context,
        ReleaseChangelog changelog, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ApplyCaching(context, changelog.Etag)) return TypedResults.StatusCode(304);
        return TypedResults.Ok(changelog.Current);
    }

    private static Results<Ok<ReleasesResponse>, StatusCodeHttpResult, ProblemHttpResult> GetHistory(HttpContext context,
        ReleaseChangelog changelog, [FromQuery] int? page, [FromQuery(Name = "page_size")] int? pageSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requestedPage = page ?? 1;
        var requestedSize = pageSize ?? 2;
        if (requestedPage < 1 || requestedSize is < 1 or > 50)
        {
            context.Response.Headers.CacheControl = "no-store";
            var failure = Result.Failure<ReleasesResponse, OperationError>(new OperationError.Validation(
                ErrorList.Single(new ModelError.InvalidValue(), requestedPage < 1 ? "page" : "page_size")));
            return failure.ToHttpResult(TypedResults.Ok, issue => new ApiIssue(issue.Error.Code,
                "Страница должна быть положительной, размер страницы — от 1 до 50.", issue.Field)).Result switch
            {
                ProblemHttpResult problem => problem,
                _ => throw new InvalidOperationException("Expected a validation problem.")
            };
        }
        if (ApplyCaching(context, changelog.Etag)) return TypedResults.StatusCode(304);
        return TypedResults.Ok(changelog.GetPage(requestedPage, requestedSize));
    }

    private static bool ApplyCaching(HttpContext context, string etag)
    {
        context.Response.Headers.CacheControl = "public, max-age=300, stale-if-error=86400";
        context.Response.Headers.ETag = etag;
        return context.Request.Headers.IfNoneMatch.Any(value => string.Equals(value, etag, StringComparison.Ordinal));
    }
}
