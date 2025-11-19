using System.Security.Claims;
using Facet.Extensions.EFCore;
using FastEndpoints;
using MelodyTrack.Common.Api.Users.Responses;
using MelodyTrack.Common.Data;
using MelodyTrack.Common.Data.Enums;
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
            AddError(_ => login, "Пользователь не авторизован");
            return TypedResults.Unauthorized();
        }

        var user = await db.Users
            .Where(u => u.Email == login.Value)
            .Include(e => e.Role)
            .FirstOrDefaultAsync(ct);

        if (user is null || !user.Role.RoleName.IsAnyAdmin())
        {
            AddError(_ => login, "Нет доступа");
            return TypedResults.Forbid();
        }

        var users = await db.Users
            .AsNoTracking()
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ToFacetsAsync<GetUsersDto>(ct);

        return TypedResults.Ok(new GetUsersResponse { Users = users });
    }
}