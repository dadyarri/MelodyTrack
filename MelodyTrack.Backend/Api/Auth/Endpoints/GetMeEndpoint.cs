using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
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
        var currentSessionIdClaim = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Sid)?.Value;

        if (email is null)
        {
            Logger.LogWarning("Profile request without valid email claim in token");
            return TypedResults.Unauthorized();
        }

        if (Ulid.TryParse(currentSessionIdClaim, out var currentSessionId))
        {
            var isSessionActive = await db.Sessions
                .AsNoTracking()
                .AnyAsync(e => e.Id == currentSessionId && !e.WasRevoked && e.ValidUntil >= DateTime.UtcNow, ct);

            if (!isSessionActive)
            {
                Logger.LogWarning("Profile request with inactive session {SessionId}", currentSessionId);
                return TypedResults.Unauthorized();
            }
        }

        var user = await db.Users
            .AsNoTracking()
            .Include(e => e.Role)
            .FirstOrDefaultAsync(e => e.Email == email, ct);

        if (user is null)
        {
            Logger.LogWarning("Profile request for non-existent user with email {Email}", email);
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
            IsTwoFactorEnabled = user.TotpSecret is not null,
            IsTwoFactorRequired = user.Role.RoleName.IsAnyAdmin()
        });
    }
}
