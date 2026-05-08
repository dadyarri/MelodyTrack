using FastEndpoints;
using MelodyTrack.Backend.Api.Roles.Responses;
using MelodyTrack.Backend.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Roles.Endpoints;

public class LookupRolesEndpoint(AppDbContext db)
    : Ep.NoReq.Res<Results<Ok<LookupRolesResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Get("/roles/lookup");
    }

    public override async Task<Results<Ok<LookupRolesResponse>, UnauthorizedHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var roles = await db.Roles
            .AsNoTracking()
            .OrderBy(e => e.DisplayName)
            .Select(e => new LookupRolesDto
            {
                Id = e.Id,
                DisplayName = e.DisplayName
            })
            .ToListAsync(ct);

        return TypedResults.Ok(new LookupRolesResponse { Roles = roles });
    }
}
