using FastEndpoints;
using MelodyTrack.Backend.Api.Users.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Users.Endpoints;

public class GetUsersEndpoint(
    AppDbContext db,
    IRecordActivityService recordActivityService,
    ICurrentUserAccessor currentUserAccessor)
    : Ep.NoReq.Res<Results<Ok<GetUsersResponse>, UnauthorizedHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/users");
    }

    public override async Task<Results<Ok<GetUsersResponse>, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var user = await currentUserAccessor.GetAsync(ct);
        if (user is null || !user.Role.RoleName.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        var users = await db.Users
            .AsNoTracking()
            .Include(e => e.Role)
            .Where(e =>
                e.Role.RoleName != UserRoles.Client &&
                (user.Role.RoleName.IsSuperuser() || e.Role.RoleName != UserRoles.Superuser))
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .Select(e => new GetUsersDto
            {
                Id = e.Id,
                LastName = e.LastName,
                FirstName = e.FirstName,
                RoleDisplayName = e.Role.DisplayName,
                Telegram = e.Telegram,
                Vk = e.Vk,
                Phone = e.Phone
            })
            .ToListAsync(ct);

        var latestActivities = await recordActivityService.GetLatestActivitiesAsync("user", users.Select(user => user.Id.ToString()).ToList(), ct);

        foreach (var item in users)
        {
            item.LastActivity = latestActivities.GetValueOrDefault(item.Id.ToString());
        }

        return TypedResults.Ok(new GetUsersResponse { Users = users });
    }
}
