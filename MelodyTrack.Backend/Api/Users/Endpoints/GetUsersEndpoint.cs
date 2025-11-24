using System.Security.Claims;
using Facet.Extensions.EFCore;
using FastEndpoints;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Api.Users.Responses;
using MelodyTrack.Common.Data;
using MelodyTrack.Common.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Users.Endpoints;

public class GetUsersEndpoint(AppDbContext db) : Ep.NoReq.Res<IResult>
{
    public override void Configure()
    {
        Get("/users");
    }

    public override async Task<IResult> ExecuteAsync(CancellationToken ct)
    {
        var login = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

        if (login is null)
        {
            AddError(_ => login, "Пользователь не авторизован");
            return ApiResults.Unauthorized();
        }

        var user = await db.Users
            .Where(u => u.Email == login.Value)
            .Include(e => e.Role)
            .FirstOrDefaultAsync(ct);

        if (user is null || !user.Role.RoleName.IsAnyAdmin())
        {
            AddError(_ => login, "Нет доступа");
            return ApiResults.Forbid();
        }

        var users = await db.Users
            .AsNoTracking()
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ToFacetsAsync<GetUsersDto>(ct);

        return ApiResults.Ok(new GetUsersResponse { Users = users });
    }
}