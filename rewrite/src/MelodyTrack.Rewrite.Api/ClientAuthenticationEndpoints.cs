using System.Security.Claims;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using IResult = Microsoft.AspNetCore.Http.IResult;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Rewrite.Api;

public sealed record ClientSecretRequest([property: JsonRequired] string? Secret)
{
    public override string ToString() => nameof(ClientSecretRequest);
}

public sealed record ClientIdentityResponse(Guid ClientId);
public sealed record ClientLinkCommand([property: JsonRequired] Guid CommandId);

public static class ClientAuthenticationEndpoints
{
    public const string ClientAccessPolicy = "ClientAccess";
    public const string ClientLinkManagementPolicy = "ClientLinkManagement";

    public static IServiceCollection AddClientAuthentication(this IServiceCollection services)
    {
        services.AddScoped<ClientAuthentication>();
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, ClientAuthenticationHandler>(ClientAuthenticationHandler.SchemeName, _ => { });
        services.AddAuthorization(options =>
        {
            options.AddPolicy(ClientAccessPolicy,
                new AuthorizationPolicyBuilder(ClientAuthenticationHandler.SchemeName).RequireAuthenticatedUser()
                    .RequireClaim(StaffIdentity.ActorKindClaim, "client").Build());
            options.AddPolicy(ClientLinkManagementPolicy, new AuthorizationPolicyBuilder(StaffAuthentication.SchemeName)
                .RequireAuthenticatedUser().AddRequirements(new StaffAccessRequirement(
                    AllowedRoles: [StaffRole.Administrator, StaffRole.Superuser])).Build());
        });
        return services;
    }

    public static IEndpointRouteBuilder MapClientAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/clients/{clientId:guid}/access-link", ReplaceLinkAsync).RequireAuthorization(ClientLinkManagementPolicy)
            .WithName("ReplaceClientAccessLink").Produces<ClientLinkSecret>().Produces(204)
            .WithDescription("Возвращает секрет только при первом исполнении. Идентичный повтор commandId возвращает 204 и не меняет доступ повторно.")
            .ProducesProblem(400).ProducesProblem(401)
            .ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        endpoints.MapDelete("/api/clients/{clientId:guid}/access-link", RevokeLinkAsync).RequireAuthorization(ClientLinkManagementPolicy)
            .WithName("RevokeClientAccessLink").Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        endpoints.MapPost("/api/auth/client/sign-in", SignInAsync).AllowAnonymous().RequireRateLimiting("client-credentials")
            .WithName("SignInClient").Produces<ClientSessionCredentials>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(429);
        endpoints.MapPost("/api/auth/client/refresh", RefreshAsync).AllowAnonymous().RequireRateLimiting("client-credentials")
            .WithName("RefreshClientAccess").Produces<ClientSessionCredentials>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(429);
        endpoints.MapPost("/api/auth/client/activate", ActivateAsync).AllowAnonymous().RequireRateLimiting("client-credentials")
            .WithName("ActivateRememberedClient").Produces<ClientSessionCredentials>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(429);
        endpoints.MapGet("/api/auth/client/me", (ClaimsPrincipal principal, HttpContext context) =>
        {
            PreventCaching(context);
            return TypedResults.Ok(new ClientIdentityResponse(ClientAuthenticationHandler.ReadIdentity(principal).ClientId.Value));
        }).RequireAuthorization(ClientAccessPolicy).WithName("GetCurrentClient").Produces<ClientIdentityResponse>().ProducesProblem(401).ProducesProblem(403);
        endpoints.MapPost("/api/auth/client/logout", SignOutAsync).RequireAuthorization(ClientAccessPolicy)
            .WithName("SignOutClient").Produces(204).ProducesProblem(401).ProducesProblem(403);
        return endpoints;
    }

    private static async Task<IResult> ReplaceLinkAsync(Guid clientId, ClientLinkCommand request,
        CurrentStaff current, ClientAuthentication authentication, HttpContext context, CancellationToken cancellationToken)
    {
        PreventCaching(context);
        var result = await authentication.ReplaceLinkAsync(new(clientId), request.CommandId, current.Actor, cancellationToken);
        if (result.IsSuccess) return result.Value switch
        {
            ClientLinkReplacement.Issued issued => TypedResults.Ok(issued.Link),
            ClientLinkReplacement.AlreadyApplied => TypedResults.NoContent()
        };
        return Result.Failure<ClientLinkSecret, OperationError>(MapError(result.Error)).ToHttpResult(TypedResults.Ok, MapIssue).Result;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeLinkAsync(Guid clientId, [FromBody] ClientLinkCommand request,
        CurrentStaff current, ClientAuthentication authentication, HttpContext context, CancellationToken cancellationToken)
    {
        PreventCaching(context);
        var result = await authentication.RevokeLinkAsync(new(clientId), request.CommandId, current.Actor, cancellationToken);
        return MapResult(result).ToHttpResult(MapIssue);
    }

    private static async Task<Results<Ok<ClientSessionCredentials>, ProblemHttpResult>> SignInAsync(ClientSecretRequest request,
        ClientAuthentication authentication, HttpContext context, CancellationToken cancellationToken)
    {
        PreventCaching(context);
        await context.RequestServices.GetRequiredService<BrowserAccessContext>().EnsureAsync(cancellationToken);
        var result = await authentication.SignInAsync(request.Secret, cancellationToken);
        return result is null ? TypedResults.Problem(statusCode: 401, title: "Доступ не подтверждён") : TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<ClientSessionCredentials>, ProblemHttpResult>> RefreshAsync(ClientSecretRequest request,
        ClientAuthentication authentication, HttpContext context, CancellationToken cancellationToken)
    {
        PreventCaching(context);
        await context.RequestServices.GetRequiredService<BrowserAccessContext>().EnsureAsync(cancellationToken);
        var result = await authentication.RefreshAsync(request.Secret, cancellationToken);
        return result is null ? TypedResults.Problem(statusCode: 401, title: "Доступ не подтверждён") : TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<ClientSessionCredentials>, ProblemHttpResult>> ActivateAsync(ClientSecretRequest request,
        ClientAuthentication authentication, HttpContext context, CancellationToken cancellationToken)
    {
        PreventCaching(context);
        await context.RequestServices.GetRequiredService<BrowserAccessContext>().EnsureAsync(cancellationToken);
        var result = await authentication.ActivateAsync(request.Secret, cancellationToken);
        return result is null ? TypedResults.Problem(statusCode: 401, title: "Доступ не подтверждён") : TypedResults.Ok(result);
    }

    private static async Task<NoContent> SignOutAsync(ClaimsPrincipal principal, ClientAuthentication authentication,
        HttpContext context, CancellationToken cancellationToken)
    {
        PreventCaching(context);
        await authentication.SignOutAsync(ClientAuthenticationHandler.ReadIdentity(principal), cancellationToken);
        return TypedResults.NoContent();
    }

    private static UnitResult<OperationError> MapResult(UnitResult<ErrorList> result) => result.IsSuccess
        ? UnitResult.Success<OperationError>() : UnitResult.Failure(MapError(result.Error));

    private static OperationError MapError(ErrorList errors)
    {
        if (errors.Issues.Any(issue => issue.Error is ModelError.Forbidden)) return new OperationError.Forbidden();
        if (errors.Issues.Any(issue => issue.Error is ModelError.NotFound)) return new OperationError.NotFound();
        if (errors.Issues.Any(issue => issue.Error is ModelError.InvalidState or ModelError.Duplicate)) return new OperationError.Conflict(errors);
        return new OperationError.Validation(errors);
    }

    private static ApiIssue MapIssue(DomainIssue issue) => new(issue.Error.Code, "Операция доступа клиента не выполнена", issue.Field);

    private static void PreventCaching(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
    }
}
