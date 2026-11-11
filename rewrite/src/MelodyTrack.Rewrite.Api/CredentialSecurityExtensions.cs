using System.Threading.RateLimiting;
using MelodyTrack.Rewrite.Application;
using Microsoft.AspNetCore.RateLimiting;

namespace MelodyTrack.Rewrite.Api;

public static class CredentialSecurityExtensions
{
    public static IServiceCollection AddCredentialSecurity(this IServiceCollection services, bool validateOnStart = true)
    {
        var passkeyOptions = services.AddOptions<PasskeyOptions>().BindConfiguration(PasskeyOptions.SectionName)
            .Validate(options => Uri.CheckHostName(options.RelyingPartyId) != UriHostNameType.Unknown
                && !string.IsNullOrWhiteSpace(options.ServerName) && options.Origins.Count > 0
                && options.Origins.All(ValidOrigin), "Authentication:Passkeys requires a relying-party domain and explicit valid origins.");
        if (validateOnStart) passkeyOptions.ValidateOnStart();
        services.AddSingleton<ICredentialSecurity, CredentialSecurity>();
        services.AddRateLimiter(options =>
        {
            AddPolicy(options, "staff-credentials", 30);
            AddPolicy(options, "client-credentials", 60);
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                await TypedResults.Problem(statusCode: 429, title: "Слишком много запросов").ExecuteAsync(context.HttpContext);
            };
        });
        return services;
    }

    private static bool ValidOrigin(string value) => Uri.TryCreate(value, UriKind.Absolute, out var origin)
        && origin.GetLeftPart(UriPartial.Authority) == value
        && (origin.Scheme == Uri.UriSchemeHttps || origin.Scheme == Uri.UriSchemeHttp && origin.IsLoopback);

    private static void AddPolicy(RateLimiterOptions options, string name, int limit) => options.AddPolicy(name, context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
        }));
}
