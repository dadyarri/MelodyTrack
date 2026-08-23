using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace MelodyTrack.Backend.Api.Auth;

public static class AuthorizationPolicies
{
    public const string ApiAccess = "api-access";
    public const string Administrator = "administrator";
    public const string Superuser = "superuser";
    public const string ClientPortal = "client-portal";
    public const string StaffOrClientPortal = "staff-or-client-portal";

    public static IServiceCollection AddDatabaseAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(ApiAccess, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new ActiveSessionRequirement(), new ApiAccessRequirement());
            });
            options.AddPolicy(Administrator, policy =>
                policy.AddRequirements(new DatabaseRoleRequirement(UserRoles.Admin | UserRoles.Superuser)));
            options.AddPolicy(Superuser, policy =>
                policy.AddRequirements(new DatabaseRoleRequirement(UserRoles.Superuser)));
            options.AddPolicy(ClientPortal, policy =>
                policy.AddRequirements(new DatabaseRoleRequirement(UserRoles.Client)));
            options.AddPolicy(StaffOrClientPortal, policy =>
                policy.AddRequirements(new DatabaseRoleRequirement(
                    UserRoles.User | UserRoles.Admin | UserRoles.Superuser | UserRoles.Client)));
        });
        services.AddScoped<IAuthorizationHandler, ActiveSessionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, ApiAccessAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, DatabaseRoleAuthorizationHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ActiveSessionAuthorizationMiddlewareResultHandler>();
        return services;
    }
}

public sealed record ActiveSessionRequirement : IAuthorizationRequirement;

public sealed record ApiAccessRequirement : IAuthorizationRequirement;

public sealed record DatabaseRoleRequirement(UserRoles AllowedRoles) : IAuthorizationRequirement;

public sealed class ActiveSessionAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure?.FailedRequirements
                .OfType<ActiveSessionRequirement>()
                .Any() == true)
        {
            return context.ChallengeAsync();
        }

        return _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}

public sealed class ActiveSessionAuthorizationHandler(ActiveSessionValidator activeSessionValidator)
    : AuthorizationHandler<ActiveSessionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveSessionRequirement requirement)
    {
        if (context.Resource is HttpContext httpContext
            && await activeSessionValidator.IsActiveAsync(httpContext, httpContext.RequestAborted))
        {
            context.Succeed(requirement);
        }
    }
}

public sealed class DatabaseRoleAuthorizationHandler(ICurrentUserAccessor currentUserAccessor)
    : AuthorizationHandler<DatabaseRoleRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        DatabaseRoleRequirement requirement)
    {
        var user = await currentUserAccessor.GetAsync(GetCancellationToken(context.Resource));
        if (user is not null && (user.Role.RoleName & requirement.AllowedRoles) != 0)
        {
            context.Succeed(requirement);
        }
    }

    private static CancellationToken GetCancellationToken(object? resource) =>
        resource is HttpContext httpContext ? httpContext.RequestAborted : CancellationToken.None;
}

public sealed class ApiAccessAuthorizationHandler(ICurrentUserAccessor currentUserAccessor)
    : AuthorizationHandler<ApiAccessRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ApiAccessRequirement requirement)
    {
        var httpContext = context.Resource as HttpContext;
        var user = await currentUserAccessor.GetAsync(httpContext?.RequestAborted ?? CancellationToken.None);
        if (user is null)
        {
            return;
        }

        if (!user.Role.RoleName.IsClient())
        {
            context.Succeed(requirement);
            return;
        }

        var endpointPolicies = httpContext?.GetEndpoint()?.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(data => data.Policy);
        if (endpointPolicies?.Any(policy => policy is AuthorizationPolicies.ClientPortal or AuthorizationPolicies.StaffOrClientPortal) == true)
        {
            context.Succeed(requirement);
        }
    }
}
