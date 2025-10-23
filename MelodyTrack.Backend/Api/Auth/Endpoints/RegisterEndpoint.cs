using System.Security.Cryptography;
using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OtpNet;
using QRCoder;

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
        var inviteCode = await db.InviteCodes
            .Include(inviteCode => inviteCode.Role)
            .FirstOrDefaultAsync(e =>
                e.Code == Ulid.Parse(req.InviteCode) && !e.WasUsed && e.ValidUntil >= DateTime.UtcNow, ct);

        if (inviteCode == null)
        {
            return TypedResults.Forbid();
        }

        var email = (string.IsNullOrEmpty(inviteCode.Email) ? req.Email : inviteCode.Email).ToLowerInvariant();

        var hasUser = await db.Users.AnyAsync(u => u.Email == email, ct);

        if (hasUser)
        {
            return TypedResults.Forbid();
        }

        UserUtils.HashPassword(email, req.Password, out var hash);

        var user = new User
        {
            Id = Ulid.NewUlid(),
            Email = email,
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

        return TypedResults.Created("/auth/register", response);
    }
}