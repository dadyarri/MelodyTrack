using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class CreateInviteEndpoint(AppDbContext db, IAuditLogService auditLogService)
    : Ep.Req<CreateInviteRequest>.Res<Results<Created<CreateInviteResponse>, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/invite");
    }

    public override async Task<Results<Created<CreateInviteResponse>, ForbidHttpResult>> ExecuteAsync(
        CreateInviteRequest req, CancellationToken ct)
    {
        var inviteEmail = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim();
        var login = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

        if (login is null)
        {
            Logger.LogWarning("Invite creation attempt without valid email claim");
            return TypedResults.Forbid();
        }

        var caller = await db.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email == login.Value, ct);

        if (caller is null || !caller.Role.RoleName.IsAnyAdmin())
        {
            Logger.LogWarning("Invite creation attempt without admin access by {Email}", login.Value);
            return TypedResults.Forbid();
        }

        var role = await db.Roles.FirstOrDefaultAsync(e => e.Id == req.Role, ct);

        if (role is null)
        {
            Logger.LogWarning("Attempt to create invite with invalid role ID {RoleId}", req.Role);
            return TypedResults.Forbid();
        }

        if (role.RoleName.IsSuperuser() && !caller.Role.RoleName.IsSuperuser())
        {
            Logger.LogWarning(
                "Admin {Email} attempted to create superuser invite without sufficient privileges",
                caller.Email);
            return TypedResults.Forbid();
        }

        var code = Ulid.NewUlid();
        var inviteUrl = UserUtils.GetInviteUrl(code);

        var invite = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = code,
            Role = role,
            Email = inviteEmail,
            ValidUntil = DateTime.UtcNow.AddDays(2)
        };

        await db.InviteCodes.AddAsync(invite, ct);
        await db.SaveChangesAsync(ct);

        var response = new CreateInviteResponse
        {
            Url = inviteUrl
        };

        Logger.LogInformation(
            "auth.invite_created actor {ActorEmail} target {Email} role {Role} url {Url}",
            caller.Email,
            inviteEmail,
            role.RoleName,
            inviteUrl);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "auth",
            Action = "invite_created",
            EntityType = "invite",
            EntityId = invite.Id.ToString(),
            Details = inviteEmail is null
                ? $"Приглашение без привязки к email с ролью {role.DisplayName}"
                : $"Приглашение для {inviteEmail} с ролью {role.DisplayName}"
        }, ct);
        return TypedResults.Created("/auth/invite", response);
    }
}
