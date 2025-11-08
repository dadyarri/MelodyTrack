using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class CreateInviteEndpoint(AppDbContext db)
    : Ep.Req<CreateInviteRequest>.Res<Results<Created<CreateInviteResponse>, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/invite");
    }

    public override async Task<Results<Created<CreateInviteResponse>, ForbidHttpResult>> ExecuteAsync(
        CreateInviteRequest req, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(e => e.Id == req.Role, ct);

        if (role is null)
        {
            Logger.LogWarning("Attempt to create invite with invalid role ID {RoleId}", req.Role);
            return TypedResults.Forbid();
        }

        var code = Ulid.NewUlid();
        var inviteUrl = UserUtils.GetInviteUrl(code);

        var invite = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = code,
            Role = role,
            Email = req.Email,
            ValidUntil = DateTime.UtcNow.AddDays(2)
        };

        await db.InviteCodes.AddAsync(invite, ct);
        await db.SaveChangesAsync(ct);

        var response = new CreateInviteResponse
        {
            Url = inviteUrl
        };

        Logger.LogInformation("Invite for user {Email} with role {Role} created: {Url}", req.Email, role.RoleName,
            inviteUrl);

        return TypedResults.Created("/auth/invite", response);
    }
}