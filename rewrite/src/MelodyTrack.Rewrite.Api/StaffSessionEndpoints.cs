using System.Text.Json;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;

namespace MelodyTrack.Rewrite.Api;

public sealed record StaffPermissionResponse(string Permission, bool Allowed, string DataScope, string TimeScope);
public sealed record CurrentStaffResponse(Guid StaffId, string Role, IReadOnlyList<StaffPermissionResponse> Permissions);

public static class StaffSessionEndpoints
{
    public static IEndpointRouteBuilder MapStaffSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth").RequireAuthorization();
        group.MapGet("/me", (HttpContext http, CurrentStaff current) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var actor = current.Actor;
            var permissions = Enum.GetValues<StaffPermission>().Select(permission =>
            {
                var grant = actor.Permissions!.For(permission);
                return new StaffPermissionResponse(Label(permission), grant.Allowed, Label(grant.DataScope), Label(grant.TimeScope));
            }).ToArray();
            return TypedResults.Ok(new CurrentStaffResponse(actor.Id.Value, Label(actor.Role), permissions));
        }).WithName("GetCurrentStaff").ProducesProblem(401).ProducesProblem(403);
        group.MapPost("/logout", async (HttpContext http, CurrentStaff current, StaffAccessTokens tokens, CancellationToken cancellationToken) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var result = await tokens.SignOutAsync(new(current.Actor.Id, current.SessionId), cancellationToken);
            return result.IsSuccess ? Results.NoContent() : Results.Problem(statusCode: 400, title: "Недопустимая сессия");
        }).WithName("SignOutCurrentStaff").Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403);
        return endpoints;
    }

    internal static string Label<T>(T value) where T : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
