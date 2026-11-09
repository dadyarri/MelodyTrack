using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace MelodyTrack.Rewrite.Api;

// One request snapshot, populated only by the authorization handler after a current database lookup.
public sealed class CurrentStaff
{
    public Actor.Staff Actor => actor ?? throw new InvalidOperationException("Current staff policy was not evaluated.");
    public Guid SessionId => actor is not null ? sessionId : throw new InvalidOperationException("Current staff policy was not evaluated.");
    private Actor.Staff? actor;
    private Guid sessionId;
    internal void Set(Actor.Staff value, Guid session)
    {
        actor = value;
        sessionId = session;
    }
}

public sealed record StaffAccessRequirement(StaffPermission? Permission = null) : IAuthorizationRequirement;

public sealed class StaffAuthorization(ICurrentStaffAccess access, CurrentStaff current)
    : AuthorizationHandler<StaffAccessRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, StaffAccessRequirement requirement)
    {
        if (context.Resource is not HttpContext http || !StaffIdentity.TryRead(context.User, out var id)) return;
        var authentication = await http.AuthenticateAsync(StaffAuthentication.SchemeName);
        if (!authentication.Succeeded || authentication.Properties is null
            || !authentication.Properties.Items.TryGetValue(StaffAuthentication.SessionProperty, out var value)
            || !Guid.TryParse(value, out var sessionId) || sessionId == Guid.Empty) return;
        var resolved = await access.ResolveAsync(id, http.RequestAborted);
        if (resolved.IsFailure || resolved.Value.Id != id || resolved.Value.Permissions is null || resolved.Value.Validate().IsFailure) return;
        var actor = resolved.Value;
        // The early gate checks the action. Data/time scopes are evaluated with the command's facts by Application.
        if (requirement.Permission is { } permission &&
            (actor.Permissions is null || !actor.Permissions.For(permission).Allowed
                || permission == StaffPermission.PoliciesManage && actor.Role != StaffRole.Superuser)) return;
        current.Set(actor, sessionId);
        context.Succeed(requirement);
    }
}

public static class StaffAuthorizationExtensions
{
    public static string Policy(StaffPermission permission) => $"staff:{permission}";

    public static IServiceCollection AddStaffAuthentication(this IServiceCollection services)
    {
        services.AddSingleton<StaffAccessTokens>();
        services.AddScoped<CurrentStaff>();
        services.AddScoped<IAuthorizationHandler, StaffAuthorization>();
        services.AddAuthentication(StaffAuthentication.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, StaffAuthentication>(StaffAuthentication.SchemeName, _ => { });
        services.AddAuthorization(options =>
        {
            var staff = new AuthorizationPolicyBuilder(StaffAuthentication.SchemeName)
                .RequireAuthenticatedUser().AddRequirements(new StaffAccessRequirement()).Build();
            options.DefaultPolicy = staff;
            options.FallbackPolicy = staff;
            foreach (var permission in Enum.GetValues<StaffPermission>())
                options.AddPolicy(Policy(permission), new AuthorizationPolicyBuilder(StaffAuthentication.SchemeName)
                    .RequireAuthenticatedUser().AddRequirements(new StaffAccessRequirement(permission)).Build());
        });
        return services;
    }
}
