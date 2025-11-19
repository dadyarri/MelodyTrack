using FastEndpoints;
using MelodyTrack.Backend.Utils;
using MelodyTrack.Common.Api.Auth.Requests;
using MelodyTrack.Common.Api.Auth.Responses;
using MelodyTrack.Common.Data;
using MelodyTrack.Common.Data.Enums;
using MelodyTrack.Common.Data.Models;
using MelodyTrack.Common.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class RegisterEndpoint(AppDbContext db)
    : Ep.Req<RegisterRequest>.Res<Results<Created<RegisterResponse>, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/register");
        AllowAnonymous();
    }

    public override async Task<Results<Created<RegisterResponse>, ForbidHttpResult>> ExecuteAsync(RegisterRequest req,
        CancellationToken ct)
    {
        Logger.LogDebug("Validating invite code {InviteCode}", req.InviteCode);

        if (!Ulid.TryParse(req.InviteCode, out var code))
        {
            Logger.LogWarning("Invalid invite code {InviteCode}", req.InviteCode);
            return TypedResults.Forbid();
        }

        var inviteCode = await db.InviteCodes
            .Include(inviteCode => inviteCode.Role)
            .FirstOrDefaultAsync(e =>
                e.Code == code && !e.WasUsed && e.ValidUntil >= DateTime.UtcNow, ct);

        if (inviteCode == null)
        {
            Logger.LogWarning("Invalid, used or expired invite code {InviteCode} provided", req.InviteCode);
            return TypedResults.Forbid();
        }

        var email = (string.IsNullOrEmpty(inviteCode.Email) ? req.Email : inviteCode.Email).ToLowerInvariant();

        var hasUser = await db.Users.AnyAsync(u => u.Email == email, ct);

        if (hasUser)
        {
            Logger.LogWarning("Attempt to register with existing email {Email}", email);
            return TypedResults.Forbid();
        }

        UserUtils.HashPassword(req.Password, out var hash);

        var user = new User
        {
            Id = Ulid.NewUlid(),
            Email = email.ToLowerInvariant(),
            FirstName = req.FirstName,
            LastName = req.LastName,
            Role = inviteCode.Role,
            Password = hash
        };

        inviteCode.WasUsed = true;

        var isTotpRequired = inviteCode.Role.RoleName.IsAnyAdmin();
        RegisterResponse? response;
        if (isTotpRequired)
        {

            var (secret, otpUrl) = UserUtils.GenerateTotp(user.Email);

            user.TotpSecret = secret;
            response = new RegisterResponse
            {
                TotpRequired = isTotpRequired,
                Secret = secret,
                OtpUrl = otpUrl
            };
        }
        else
        {
            response = new RegisterResponse
            {
                TotpRequired = isTotpRequired
            };
        }

        await db.Users.AddAsync(user, ct);
        await db.SaveChangesAsync(ct);

        Logger.LogInformation("Successfully registered new user {Email} with role {Role}", email, inviteCode.Role.RoleName);
        if (isTotpRequired)
        {
            Logger.LogInformation("2FA setup required for user {Email} due to admin role", email);
        }

        return TypedResults.Created("/auth/register", response);
    }
}