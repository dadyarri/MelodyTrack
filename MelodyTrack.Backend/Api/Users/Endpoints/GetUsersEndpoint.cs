using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Api.Users.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Users.Endpoints;

public class GetUsersEndpoint(AppDbContext db) : Ep.NoReq.Res<Results<Ok<GetUsersResponse>, UnauthorizedHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/users");
    }

    public override async Task<Results<Ok<GetUsersResponse>, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var login = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

        if (login is null)
        {
            return TypedResults.Unauthorized();
        }

        var user = await db.Users
            .Where(u => u.Email == login.Value)
            .Include(e => e.Role)
            .FirstOrDefaultAsync(ct);

        if (user is null || !user.Role.RoleName.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        var users = await db.Users
            .AsNoTracking()
            .Include(e => e.Role)
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

        return TypedResults.Ok(new GetUsersResponse { Users = users });
    }
}
