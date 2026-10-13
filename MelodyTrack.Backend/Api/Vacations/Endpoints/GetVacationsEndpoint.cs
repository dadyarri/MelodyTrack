using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.Vacations.Requests;
using MelodyTrack.Backend.Api.Vacations.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Vacations.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/vacations")]
public sealed class GetVacationsEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.StaffOrClientPortal)]
    public static async Task<Results<Ok<GetVacationsResponse>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        [AsParameters] GetVacationsRequest input, AppDbContext db, VacationService vacations,
        ICurrentUserAccessor currentUserAccessor, TimeProvider timeProvider, CancellationToken ct)
    {
        var actor = await currentUserAccessor.GetAsync(ct);
        if (actor is null) return TypedResults.Unauthorized();
        var client = actor.Role.RoleName.IsClient();
        var subjectType = input.SubjectType ?? (client ? "client" : "staff");
        var subjectId = input.SubjectId ?? (client ? actor.ClientId : actor.Id);
        if (subjectId is null || subjectType is not ("client" or "staff")) return TypedResults.Forbid();
        if (!actor.Role.RoleName.IsAnyAdmin() && (subjectId != (client ? actor.ClientId : actor.Id) || subjectType != (client ? "client" : "staff")))
            return TypedResults.Forbid();
        if (subjectType == "staff" && !actor.Role.RoleName.IsSuperuser()
            && await db.Users.AnyAsync(item => item.Id == subjectId && item.Role.RoleName == UserRoles.Superuser, ct)) return TypedResults.Forbid();
        var query = db.Vacations.IgnoreQueryFilters().Include(item => item.User).AsNoTracking()
            .Where(item => subjectType == "client" ? item.ClientId == subjectId : item.UserId == subjectId);
        if (!input.IncludeHistory)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            query = query.Where(item => item.DeletedAtUtc == null && item.EndedAtUtc == null && item.EndDate > now);
        }
        var rows = await query.OrderByDescending(item => item.StartDate).ToListAsync(ct);
        var mapped = new List<VacationResponse>();
        foreach (var row in rows) mapped.Add(await vacations.MapAsync(row, actor, ct));
        return TypedResults.Ok(new GetVacationsResponse { Items = mapped });
    }
}
