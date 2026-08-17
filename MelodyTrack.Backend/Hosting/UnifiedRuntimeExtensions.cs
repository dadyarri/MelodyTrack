using MelodyTrack.Backend.ErrorHandling;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;

namespace MelodyTrack.Backend.Hosting;

public static class UnifiedRuntimeExtensions
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; " +
        "script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; " +
        "connect-src 'self' https:; manifest-src 'self'";
    private const string ImmutableCacheControl = "public, max-age=31536000, immutable";
    private const string EntryPointCacheControl = "no-cache";
    private static readonly PathString[] SpaFallbackExclusions = ["/api", "/otel", "/health", "/alive", "/swagger", "/openapi"];

    public static WebApplication UseUnifiedRuntimeHeaders(this WebApplication app, PathString apiPathBase = default)
    {
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.TryAdd(HeaderNames.ContentSecurityPolicy, ContentSecurityPolicy);
                headers.TryAdd(HeaderNames.XContentTypeOptions, "nosniff");
                headers.TryAdd("X-Frame-Options", "DENY");
                headers.TryAdd("Referrer-Policy", "no-referrer");
                headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
                headers.TryAdd("X-Trace-Id", context.TraceIdentifier);

                if (context.Response.StatusCode >= StatusCodes.Status400BadRequest
                    && context.Response.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true)
                {
                    context.Response.ContentType = ApiMediaTypes.ProblemJson;
                }

                if (ShouldDisableCaching(context.Request.Path, apiPathBase) || headers.ContainsKey(HeaderNames.ContentDisposition))
                {
                    SetNoStore(headers);
                }

                return Task.CompletedTask;
            });

            await next();
        });

        return app;
    }

    public static WebApplication UseSpaStaticFiles(this WebApplication app)
    {
        var contentTypes = new FileExtensionContentTypeProvider();
        contentTypes.Mappings[".webmanifest"] = "application/manifest+json";

        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            ContentTypeProvider = contentTypes,
            OnPrepareResponse = context => ApplyStaticCachePolicy(context.Context, context.File)
        });
        return app;
    }

    public static IEndpointConventionBuilder MapSpaFallback(this WebApplication app)
    {
        return app.MapFallback(async context =>
        {
            if (!CanUseSpaFallback(context.Request))
            {
                await WriteNotFoundAsync(context);
                return;
            }

            var indexFile = app.Environment.WebRootFileProvider.GetFileInfo("index.html");
            if (!indexFile.Exists)
            {
                await WriteNotFoundAsync(context);
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = EntryPointCacheControl;
            await context.Response.SendFileAsync(indexFile, context.RequestAborted);
        });
    }

    private static bool CanUseSpaFallback(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            return false;
        }

        if (SpaFallbackExclusions.Any(exclusion => request.Path.StartsWithSegments(exclusion)))
        {
            return false;
        }

        return !Path.HasExtension(request.Path.Value);
    }

    private static void ApplyStaticCachePolicy(HttpContext context, IFileInfo file)
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/assets"))
        {
            context.Response.Headers.CacheControl = ImmutableCacheControl;
            return;
        }

        if (file.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase)
            || file.Name.Equals("sw.js", StringComparison.OrdinalIgnoreCase)
            || file.Name.Equals("service-worker.js", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Headers.CacheControl = EntryPointCacheControl;
            return;
        }

        if (Path.GetExtension(file.Name) is ".ico" or ".png" or ".svg" or ".webmanifest")
        {
            context.Response.Headers.CacheControl = ImmutableCacheControl;
        }
    }

    private static bool ShouldDisableCaching(PathString path, PathString apiPathBase)
    {
        if (apiPathBase.HasValue && path.StartsWithSegments(apiPathBase, out var pathWithoutPrefix))
        {
            path = pathWithoutPrefix;
        }

        if (path.StartsWithSegments("/auth"))
        {
            return true;
        }

        return path.StartsWithSegments("/users", out var remainingPath)
               && remainingPath.Value?.EndsWith("/password-reset-links", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static void SetNoStore(IHeaderDictionary headers)
    {
        headers.CacheControl = "no-store, no-cache, max-age=0";
        headers.Pragma = "no-cache";
        headers.Expires = "0";
    }

    private static async Task WriteNotFoundAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = ApiMediaTypes.ProblemJson;
        var problemDetails = ApiErrorResponseFactory.CreateProblemDetails(context, StatusCodes.Status404NotFound);
        await problemDetails.ExecuteAsync(context);
    }
}
