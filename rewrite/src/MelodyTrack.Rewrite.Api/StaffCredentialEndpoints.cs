using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Persistence.Access;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Rewrite.Api;

public sealed record StaffInvitationRequest([property: JsonRequired] string? Role);
public sealed record StaffRegistrationInvitationRequest([property: JsonRequired] string? Link)
{
    public override string ToString() => nameof(StaffRegistrationInvitationRequest);
}
public sealed record StaffRegistrationInvitationResponse(string Role);
public sealed record StaffRegistrationRequest([property: JsonRequired] string? Link, [property: JsonRequired] string? Login,
    string? Password, bool Authenticator = false, bool SecondFactor = false)
{
    public override string ToString() => nameof(StaffRegistrationRequest);
}
public sealed record StaffRegistrationCompleteRequest([property: JsonRequired] string? Challenge,
    string? AuthenticatorCode = null, JsonElement? PasskeyResponse = null)
{
    public override string ToString() => nameof(StaffRegistrationCompleteRequest);
}
public sealed record StaffRegistrationAuthenticatorRequest([property: JsonRequired] string? Challenge,
    [property: JsonRequired] bool Skip, string? Code = null)
{
    public override string ToString() => nameof(StaffRegistrationAuthenticatorRequest);
}
public sealed record StaffPasswordRequest([property: JsonRequired] string? Login, [property: JsonRequired] string? Password)
{
    public override string ToString() => nameof(StaffPasswordRequest);
}
public sealed record StaffPasskeyCompleteRequest([property: JsonRequired] string? Challenge, [property: JsonRequired] JsonElement Response)
{
    public override string ToString() => nameof(StaffPasskeyCompleteRequest);
}
public sealed record StaffFactorRequest([property: JsonRequired] string? Challenge, string? Code = null, JsonElement? PasskeyResponse = null)
{
    public override string ToString() => nameof(StaffFactorRequest);
}
public sealed record StaffRefreshRequest([property: JsonRequired] string? RefreshToken)
{
    public override string ToString() => nameof(StaffRefreshRequest);
}
public sealed record StaffPasswordChangeRequest(string? OldPassword, [property: JsonRequired] string? NewPassword)
{
    public override string ToString() => nameof(StaffPasswordChangeRequest);
}
public sealed record StaffAuthenticatorConfirmationRequest([property: JsonRequired] string? Challenge, [property: JsonRequired] string? Code)
{
    public override string ToString() => nameof(StaffAuthenticatorConfirmationRequest);
}
public sealed record StaffAuthenticatorCodeRequest([property: JsonRequired] string? Code)
{
    public override string ToString() => nameof(StaffAuthenticatorCodeRequest);
}
public sealed record StaffSecondFactorRequest([property: JsonRequired] bool Enabled);
public sealed record StaffAccessChangeRequest(string? Role = null, bool? Blocked = null);
public sealed record StaffSecuritySessionResponse(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset RefreshExpiresAt,
    bool IsCurrent, bool Revoked);
public sealed record StaffSecurityProfileResponse(bool HasPassword, bool AuthenticatorEnabled, bool SecondFactorEnabled,
    IReadOnlyList<string> Passkeys, IReadOnlyList<StaffSecuritySessionResponse> Sessions);

public static class StaffCredentialEndpoints
{
    public static IServiceCollection AddStaffCredentialFlows(this IServiceCollection services)
    {
        services.AddScoped<IStaffCredentialStore, StaffCredentialStore>();
        services.AddScoped<StaffCredentialFlows>();
        services.AddScoped<ICurrentStaffLogin>(provider => provider.GetRequiredService<StaffCredentialFlows>());
        return services;
    }

    public static IEndpointRouteBuilder MapStaffCredentialEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var routes = endpoints.MapGroup("/api/auth/staff");
        routes.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            await context.HttpContext.RequestServices.GetRequiredService<BrowserAccessContext>().EnsureAsync(context.HttpContext.RequestAborted);
            return await next(context);
        });

        Public(routes.MapPost("/register/invitation", async (StaffRegistrationInvitationRequest request, StaffCredentialFlows flows, CancellationToken ct) =>
        {
            if (!Present(request.Link)) return Unauthorized<StaffRegistrationInvitationResponse>();
            var invitation = await flows.PreviewInvitationAsync(request.Link!, ct);
            return Map(invitation.Map(role => new StaffRegistrationInvitationResponse(StaffSessionEndpoints.Label(role))), true);
        }));
        Public(routes.MapPost("/register/options", async (StaffRegistrationRequest request, StaffCredentialFlows flows, CancellationToken ct) =>
            !Present(request.Link) || !Present(request.Login) ? Unauthorized<StaffCredentialResponse>()
                : Map(await flows.BeginRegistrationAsync(request.Link!, request.Login!, request.Password, request.Authenticator, request.SecondFactor, ct), true)));
        Public(routes.MapPost("/register/authenticator", async (StaffRegistrationAuthenticatorRequest request, StaffCredentialFlows flows, CancellationToken ct) =>
            !Present(request.Challenge) ? Unauthorized<StaffCredentialResponse>()
                : Map(await flows.AdvanceRegistrationAuthenticatorAsync(request.Challenge!, request.Code, request.Skip, ct), true)));
        Public(routes.MapPost("/register/complete", async (StaffRegistrationCompleteRequest request, StaffCredentialFlows flows, CancellationToken ct) =>
            !Present(request.Challenge) ? Unauthorized<StaffCredentialResponse>()
                : Map(await flows.CompleteRegistrationAsync(request.Challenge!, request.AuthenticatorCode, Json(request.PasskeyResponse), ct), true)));
        Public(routes.MapPost("/password", async (StaffPasswordRequest request, StaffCredentialFlows flows, CancellationToken ct) =>
            !Present(request.Login) || !Present(request.Password) ? Unauthorized<StaffCredentialResponse>()
                : Map(await flows.PasswordAsync(request.Login!, request.Password!, ct), true)));
        Public(routes.MapPost("/passkey/options", async (StaffCredentialFlows flows, CancellationToken ct) =>
            Map(await flows.BeginPasskeyAsync(ct), true)));
        Public(routes.MapPost("/passkey/complete", async (StaffPasskeyCompleteRequest request, StaffCredentialFlows flows, CancellationToken ct) =>
            !Present(request.Challenge) || Json(request.Response) is not { } response ? Unauthorized<StaffCredentialResponse>()
                : Map(await flows.CompletePasskeyAsync(request.Challenge!, response, ct), true)));
        Public(routes.MapPost("/factor", async (StaffFactorRequest request, StaffCredentialFlows flows, CancellationToken ct) =>
            !Present(request.Challenge) ? Unauthorized<StaffCredentialResponse>()
                : Map(await flows.FactorAsync(request.Challenge!, request.Code, Json(request.PasskeyResponse), ct), true)));
        Public(routes.MapPost("/refresh", async (StaffRefreshRequest request, StaffCredentialFlows flows, CancellationToken ct) =>
            !Present(request.RefreshToken) ? Unauthorized<StaffCredentialResponse>() : Map(await flows.RefreshAsync(request.RefreshToken!, false, ct), true)));
        Public(routes.MapPost("/resume", async (StaffRefreshRequest request, StaffCredentialFlows flows, CancellationToken ct) =>
            !Present(request.RefreshToken) ? Unauthorized<StaffCredentialResponse>() : Map(await flows.RefreshAsync(request.RefreshToken!, true, ct), true)));

        var own = routes.MapGroup("").RequireAuthorization();
        Describe(own.MapGet("/security", async (CurrentStaff current, StaffCredentialFlows flows, CancellationToken ct) =>
        {
            var profile = await flows.ProfileAsync(current.Actor.Id, ct);
            if (profile.IsFailure) return Map(Result.Failure<StaffSecurityProfileResponse, ErrorList>(profile.Error));
            var value = profile.Value;
            return Map(Result.Success<StaffSecurityProfileResponse, ErrorList>(new(value.HasPassword, value.AuthenticatorEnabled,
                value.SecondFactorEnabled, value.Passkeys, value.Sessions.Select(session => new StaffSecuritySessionResponse(
                    session.Id, session.CreatedAt, session.RefreshExpiresAt, session.Id == current.SessionId, session.Revoked)).ToArray())));
        }));
        Describe(own.MapPut("/security/password", async (StaffPasswordChangeRequest request, CurrentStaff current, StaffCredentialFlows flows, CancellationToken ct) =>
            !Present(request.NewPassword) ? Invalid<StaffCredentialResponse>()
                : Map(await flows.ChangePasswordAsync(current.Actor.Id, request.OldPassword, request.NewPassword!, ct))));
        Describe(own.MapPost("/security/authenticator/options", async (CurrentStaff current, StaffCredentialFlows flows, CancellationToken ct) =>
            Map(await flows.BeginAuthenticatorAsync(current.Actor.Id, ct))));
        Describe(own.MapPost("/security/authenticator/confirm", async (StaffAuthenticatorConfirmationRequest request, CurrentStaff current, StaffCredentialFlows flows, CancellationToken ct) =>
            !Present(request.Challenge) || !Present(request.Code) ? Invalid<StaffCredentialResponse>()
                : Map(await flows.ConfirmAuthenticatorAsync(current.Actor.Id, request.Challenge!, request.Code!, ct))));
        Describe(own.MapDelete("/security/authenticator", async ([Microsoft.AspNetCore.Mvc.FromBody] StaffAuthenticatorCodeRequest request, CurrentStaff current, StaffCredentialFlows flows, CancellationToken ct) =>
            !Present(request.Code) ? Invalid<StaffCredentialResponse>() : Map(await flows.DisableAuthenticatorAsync(current.Actor.Id, request.Code!, ct))));
        Describe(own.MapPut("/security/second-factor", async (StaffSecondFactorRequest request, CurrentStaff current, StaffCredentialFlows flows, CancellationToken ct) =>
            Map(await flows.SetSecondFactorAsync(current.Actor.Id, request.Enabled, ct))));
        Describe(own.MapPost("/security/passkeys/options", async (CurrentStaff current, StaffCredentialFlows flows, CancellationToken ct) =>
            Map(await flows.BeginAddPasskeyAsync(current.Actor.Id, ct))));
        Describe(own.MapPost("/security/passkeys/complete", async (StaffPasskeyCompleteRequest request, CurrentStaff current, StaffCredentialFlows flows, CancellationToken ct) =>
            !Present(request.Challenge) || Json(request.Response) is not { } response ? Invalid<StaffCredentialResponse>()
                : Map(await flows.CompleteAddPasskeyAsync(current.Actor.Id, request.Challenge!, response, ct))));
        Describe(own.MapDelete("/security/passkeys/{credentialId}", async (string credentialId, CurrentStaff current, StaffCredentialFlows flows, CancellationToken ct) =>
            Map(await flows.RemovePasskeyAsync(current.Actor.Id, credentialId, ct))));
        Describe(own.MapDelete("/sessions", async (CurrentStaff current, StaffCredentialFlows flows, CancellationToken ct) =>
            Map(await flows.RevokeAsync(current.Actor.Id, null, ct))));
        Describe(own.MapDelete("/sessions/{sessionId:guid}", async (Guid sessionId, CurrentStaff current, StaffCredentialFlows flows, CancellationToken ct) =>
            sessionId == Guid.Empty ? Invalid<StaffCredentialResponse>() : Map(await flows.RevokeAsync(current.Actor.Id, sessionId, ct))));

        var manage = routes.MapGroup("").RequireAuthorization(StaffAuthorizationExtensions.Policy(StaffPermission.PoliciesManage));
        Describe(manage.MapPost("/invitations", async (StaffInvitationRequest request, CurrentStaff current, StaffCredentialFlows flows, CancellationToken ct) =>
            !TryRole(request.Role, out var role) ? Invalid<StaffCredentialResponse>() : Map(await flows.InviteAsync(current.Actor, role, null, ct))));
        Describe(manage.MapPost("/{staffId:guid}/recovery", async (Guid staffId, CurrentStaff current, StaffCredentialFlows flows, CancellationToken ct) =>
            staffId == Guid.Empty ? Invalid<StaffCredentialResponse>() : Map(await flows.InviteAsync(current.Actor, StaffRole.Teacher, staffId, ct))));
        Describe(manage.MapPut("/{staffId:guid}/access", async (Guid staffId, StaffAccessChangeRequest request, CurrentStaff current, StaffCredentialFlows flows, CancellationToken ct) =>
        {
            if (staffId == Guid.Empty || request.Role is not null && !TryRole(request.Role, out _)) return Invalid<StaffCredentialResponse>();
            StaffRole? role = request.Role is null ? null : Enum.Parse<StaffRole>(request.Role, true);
            return Map(await flows.SetAccessAsync(current.Actor, new(staffId), role, request.Blocked, ct));
        }));
        return endpoints;
    }

    private static void Public(RouteHandlerBuilder endpoint) => Describe(endpoint.AllowAnonymous().RequireRateLimiting("staff-credentials"));
    private static void Describe(RouteHandlerBuilder endpoint) => endpoint.ProducesProblem(400).ProducesProblem(401)
        .ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(429);
    private static bool Present(string? value) => !string.IsNullOrWhiteSpace(value);
    private static string? Json(JsonElement? value) => value is { ValueKind: JsonValueKind.Object } json ? json.GetRawText() : null;
    private static bool TryRole(string? value, out StaffRole role)
    {
        foreach (var candidate in Enum.GetValues<StaffRole>())
            if (StaffSessionEndpoints.Label(candidate) == value) { role = candidate; return true; }
        role = default;
        return false;
    }
    private static Results<Ok<T>, ProblemHttpResult> Invalid<T>() => Failure<T>(new OperationError.Validation(ErrorList.Single(new ModelError.InvalidValue(), "request")));
    private static Results<Ok<T>, ProblemHttpResult> Unauthorized<T>() => Failure<T>(new OperationError.Unauthorized());
    private static Results<Ok<T>, ProblemHttpResult> Failure<T>(OperationError error) => Result.Failure<T, OperationError>(error)
        .ToHttpResult(TypedResults.Ok, issue => new ApiIssue(issue.Error.Code, "Операция аутентификации не выполнена", issue.Field));
    private static Results<Ok<StaffCredentialResponse>, ProblemHttpResult> Map(Result<StaffCredentialResult, ErrorList> result,
        bool credentials = false) => Map(result.IsSuccess
            ? Result.Success<StaffCredentialResponse, ErrorList>(StaffCredentialResponse.From(result.Value))
            : Result.Failure<StaffCredentialResponse, ErrorList>(result.Error), credentials);

    private static Results<Ok<T>, ProblemHttpResult> Map<T>(Result<T, ErrorList> result, bool credentials = false)
    {
        if (result.IsSuccess) return TypedResults.Ok(result.Value);
        var errors = result.Error;
        if (errors.Issues.Any(issue => issue.Error is ModelError.Forbidden)) return Failure<T>(new OperationError.Forbidden());
        if (credentials) return Unauthorized<T>();
        if (errors.Issues.Any(issue => issue.Error is ModelError.NotFound)) return Failure<T>(new OperationError.NotFound());
        if (errors.Issues.Any(issue => issue.Error is ModelError.Duplicate or ModelError.InvalidState)) return Failure<T>(new OperationError.Conflict(errors));
        return Failure<T>(new OperationError.Validation(errors));
    }
}
