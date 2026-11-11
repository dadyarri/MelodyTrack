using System.Text.Json;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Rewrite.Api;

public sealed record StaffPermissionResponse(string Permission, bool Allowed, string DataScope, string TimeScope);
public sealed record CurrentStaffResponse(Guid StaffId, string Login, string Role, IReadOnlyList<StaffPermissionResponse> Permissions)
{
    public override string ToString() => nameof(CurrentStaffResponse);
}

public static class StaffSessionEndpoints
{
    public static IEndpointRouteBuilder MapStaffSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth").RequireAuthorization();
        group.MapGet("/me", GetCurrentStaffAsync).WithName("GetCurrentStaff").ProducesProblem(401).ProducesProblem(403);
        group.MapPost("/logout", async (HttpContext http, CurrentStaff current, StaffAccessTokens tokens, CancellationToken cancellationToken) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var result = await tokens.SignOutAsync(new(current.Actor.Id, current.SessionId), cancellationToken);
            return result.IsSuccess ? Results.NoContent() : Results.Problem(statusCode: 400, title: "Недопустимая сессия");
        }).WithName("SignOutCurrentStaff").Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403);
        return endpoints;
    }

    private static async Task<Results<Ok<CurrentStaffResponse>, ProblemHttpResult>> GetCurrentStaffAsync(
        HttpContext http, CurrentStaff current, ICurrentStaffLogin logins, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var actor = current.Actor;
        var login = await logins.GetLoginAsync(actor.Id, cancellationToken);
        if (login.IsFailure)
        {
            http.Response.Headers.WWWAuthenticate = "Bearer";
            return TypedResults.Problem(statusCode: 401, title: "Требуется вход");
        }
        var permissions = Enum.GetValues<StaffPermission>().Select(permission =>
        {
            var grant = actor.Permissions!.For(permission);
            return new StaffPermissionResponse(Label(permission), grant.Allowed, Label(grant.DataScope), Label(grant.TimeScope));
        }).ToArray();
        return TypedResults.Ok(new CurrentStaffResponse(actor.Id.Value, login.Value, Label(actor.Role), permissions));
    }

    internal static string Label<T>(T value) where T : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
