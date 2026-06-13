using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class GetMeEndpoint(AppDbContext db, IRecordActivityService recordActivityService)
    : Ep.NoReq.Res<Results<Ok<MeResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Get("/auth/me");
    }

    public override async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;

        if (email is null)
        {
            Logger.LogWarning("Profile request without valid email claim in token");
            return TypedResults.Unauthorized();
        }

        var user = await db.Users
            .AsNoTracking()
            .Include(e => e.Role)
            .WhereEmailMatches(email)
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            Logger.LogWarning("Profile request for non-existent {EmailRef}", UserUtils.DescribeEmailForLogs(email));
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new MeResponse
        {
            Id = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            RoleDisplayName = user.Role.DisplayName,
            Phone = user.Phone,
            Telegram = user.Telegram,
            Vk = user.Vk,
            LastActivity = await recordActivityService.GetLatestActivityAsync("user", user.Id.ToString(), ct),
            IsAdmin = user.Role.RoleName.IsAnyAdmin(),
            IsSuperuser = user.Role.RoleName.IsSuperuser(),
            IsClientPortal = user.Role.RoleName.IsClient(),
            LinkedClientId = user.ClientId,
            IsTwoFactorEnabled = user.TotpSecret is not null,
            IsTwoFactorRequired = user.Role.RoleName.IsAnyAdmin()
        });
    }
}
