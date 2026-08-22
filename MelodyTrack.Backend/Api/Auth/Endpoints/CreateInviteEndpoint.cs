using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/auth/invites")]
public sealed class CreateInviteEndpoint
{

    public static async Task<Results<Created<CreateInviteResponse>, ForbidHttpResult>> HandleAsync(
        CreateInviteRequest req,
        AppDbContext db,
        IAuditLogService auditLogService,
        IPublicUrlBuilder publicUrlBuilder,
        TimeProvider timeProvider,
        ICurrentUserAccessor currentUserAccessor,
        ILogger<CreateInviteEndpoint> logger,
        CancellationToken ct
    )
    {
        var inviteEmail = string.IsNullOrWhiteSpace(req.Email) ? null : UserUtils.NormalizeEmail(req.Email);
        var caller = await currentUserAccessor.GetAsync(ct);

        if (caller is null || !caller.Role.RoleName.IsAnyAdmin())
        {
            logger.LogWarning("Invite creation attempt without admin access");
            return TypedResults.Forbid();
        }

        var role = await db.Roles.FirstOrDefaultAsync(e => e.Id == req.Role, ct);

        if (role is null)
        {
            logger.LogWarning("Attempt to create invite with invalid role ID {RoleId}", req.Role);
            return TypedResults.Forbid();
        }

        if (role.RoleName.IsSuperuser() && !caller.Role.RoleName.IsSuperuser())
        {
            logger.LogWarning(
                "Admin {EmailRef} attempted to create superuser invite without sufficient privileges",
                UserUtils.DescribeEmailForLogs(caller.Email));
            return TypedResults.Forbid();
        }

        if (role.RoleName.IsClient())
        {
            logger.LogWarning("Attempt to create client portal user through generic invite flow by {EmailRef}", UserUtils.DescribeEmailForLogs(caller.Email));
            return TypedResults.Forbid();
        }

        var code = Ulid.NewUlid();
        var inviteUrl = publicUrlBuilder.GetInviteUrl(code);

        var invite = new InviteCode
        {
            Id = Ulid.NewUlid(),
            Code = code,
            Role = role,
            Email = inviteEmail,
            ValidUntil = timeProvider.GetUtcNow().UtcDateTime.AddDays(2)
        };

        await db.InviteCodes.AddAsync(invite, ct);
        await db.SaveChangesAsync(ct);

        var response = new CreateInviteResponse
        {
            Url = inviteUrl
        };
        var inviteRef = UserUtils.DescribeInviteCodeForLogs(invite.Code);

        logger.LogInformation(
            "auth.invite_created actor {ActorEmailRef} target {TargetEmailRef} role {Role} invite {InviteRef}",
            UserUtils.DescribeEmailForLogs(caller.Email),
            UserUtils.DescribeEmailForLogs(inviteEmail),
            role.RoleName,
            inviteRef);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "auth",
            Action = "invite_created",
            EntityType = "invite",
            EntityId = invite.Id.ToString(),
            Details = inviteEmail is null
                ? $"Приглашение {inviteRef} без привязки к email с ролью {role.DisplayName}"
                : $"Приглашение {inviteRef} для {UserUtils.DescribeEmailForLogs(inviteEmail)} с ролью {role.DisplayName}"
        }, ct);
        return TypedResults.Created("/auth/invites", response);
    }
}
