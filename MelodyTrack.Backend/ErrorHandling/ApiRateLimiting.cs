using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace MelodyTrack.Backend.ErrorHandling;

public static class ApiRateLimitPolicies
{
    public const string Login = "auth-login";
    public const string Register = "auth-register";
    public const string Refresh = "auth-refresh";
    public const string VerifyTwoFactor = "auth-verify-2fa";
    public const string RecoverTwoFactor = "auth-recover-2fa";
    public const string ResetPassword = "auth-reset-password";
    public const string InviteInformation = "auth-invite-information";
    public const string PortalLinkStatus = "portal-link-status";
    public const string PortalAuthentication = "portal-authentication";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds)
                        .ToString(CultureInfo.InvariantCulture);
                }

                var problem = ApiErrorResponseFactory.CreateProblemDetails(
                    context.HttpContext,
                    StatusCodes.Status429TooManyRequests);
                await problem.ExecuteAsync(context.HttpContext);
            };

            AddPolicy(options, Login, 10, TimeSpan.FromMinutes(1));
            AddPolicy(options, Register, 10, TimeSpan.FromMinutes(5));
            AddPolicy(options, Refresh, 30, TimeSpan.FromMinutes(1));
            AddPolicy(options, VerifyTwoFactor, 10, TimeSpan.FromMinutes(5));
            AddPolicy(options, RecoverTwoFactor, 10, TimeSpan.FromMinutes(5));
            AddPolicy(options, ResetPassword, 10, TimeSpan.FromMinutes(10));
            AddPolicy(options, InviteInformation, 30, TimeSpan.FromMinutes(1));
            AddPolicy(options, PortalLinkStatus, 60, TimeSpan.FromMinutes(1));
            AddPolicy(options, PortalAuthentication, 20, TimeSpan.FromMinutes(1));
        });

        return services;
    }

    private static void AddPolicy(RateLimiterOptions options, string name, int permitLimit, TimeSpan window)
    {
        options.AddPolicy(name, context => GetFixedWindowPartition(context, permitLimit, window));
    }

    private static RateLimitPartition<string> GetFixedWindowPartition(HttpContext context, int permitLimit, TimeSpan window)
    {
        var partitionKey = $"{context.Request.Method}:{context.Request.Path}:{GetPartitionKey(context)}";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
                AutoReplenishment = true
            });
    }

    private static string GetPartitionKey(HttpContext context) =>
        context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor)
            ? forwardedFor.ToString()
            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
