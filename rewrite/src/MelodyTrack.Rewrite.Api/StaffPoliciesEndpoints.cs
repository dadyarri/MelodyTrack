using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Rewrite.Api;

public sealed record PermissionGrantRequest([property: JsonRequired] bool Allowed, string? DataScope, string? TimeScope);
public sealed record StaffPolicyPutRequest(Guid CommandId, [property: JsonRequired] PermissionGrantRequest? Grant);

public static class StaffPoliciesEndpoints
{
    public static IEndpointRouteBuilder MapStaffPoliciesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/access")
            .RequireAuthorization(StaffAuthorizationExtensions.Policy(StaffPermission.PoliciesManage));
        Describe(group.MapPut("/roles/{role}/permissions/{permission}", async (string role, string permission,
            StaffPolicyPutRequest request, CurrentStaff current, StaffPolicies policies, CancellationToken cancellationToken) =>
        {
            if (request.CommandId == Guid.Empty || !TryEnum<StaffRole>(role, out var target)
                || !TryEnum<StaffPermission>(permission, out var action) || request.Grant is null || !TryGrant(request.Grant, out var grant))
                return Failure(new OperationError.Validation(ErrorList.Single(new ModelError.InvalidValue(), "request")));
            return Map(await policies.SetRoleAsync(request.CommandId, current.Actor, target, action, grant!, cancellationToken));
        }).WithName("SetStaffRolePermission"));
        Describe(group.MapPut("/staff/{staffId:guid}/permissions/{permission}", async (Guid staffId, string permission,
            StaffPolicyPutRequest request, CurrentStaff current, StaffPolicies policies, CancellationToken cancellationToken) =>
        {
            if (request.CommandId == Guid.Empty || staffId == Guid.Empty || !TryEnum<StaffPermission>(permission, out var action)
                || !TryGrant(request.Grant, out var grant))
                return Failure(new OperationError.Validation(ErrorList.Single(new ModelError.InvalidValue(), "request")));
            return Map(await policies.SetPersonalAsync(request.CommandId, current.Actor, new(staffId), action, grant, cancellationToken));
        }).WithName("SetPersonalStaffPermission"));
        return endpoints;
    }

    private static void Describe(RouteHandlerBuilder endpoint) => endpoint.Produces(204)
        .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

    private static bool TryGrant(PermissionGrantRequest? request, out PermissionGrant? grant)
    {
        grant = null;
        if (request is null) return true;
        if (!TryEnum<PermissionDataScope>(request.DataScope, out var data) || !TryEnum<PermissionTimeScope>(request.TimeScope, out var time)) return false;
        grant = new(request.Allowed, data, time);
        return true;
    }

    private static bool TryEnum<T>(string? text, out T value) where T : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<T>())
            if (StaffSessionEndpoints.Label(candidate).Equals(text, StringComparison.Ordinal)) { value = candidate; return true; }
        value = default;
        return false;
    }

    private static Results<NoContent, ProblemHttpResult> Map(UnitResult<ErrorList> result)
    {
        if (result.IsSuccess) return TypedResults.NoContent();
        var errors = result.Error;
        if (errors.Issues.Any(issue => issue.Error is ModelError.Forbidden)) return Failure(new OperationError.Forbidden());
        if (errors.Issues.Any(issue => issue.Error is ModelError.NotFound)) return Failure(new OperationError.NotFound());
        if (errors.Issues.Any(issue => issue.Error is ModelError.InvalidState or ModelError.Duplicate)) return Failure(new OperationError.Conflict(errors));
        return Failure(new OperationError.Validation(errors));
    }

    private static Results<NoContent, ProblemHttpResult> Failure(OperationError error) =>
        UnitResult.Failure<OperationError>(error).ToHttpResult(issue => new ApiIssue(issue.Error.Code, "Политика доступа не изменена", issue.Field));
}
