using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class RecoveryCodesEndpoint(AppDbContext db) : Ep.NoReq.Res<Results<Ok<RecoveryCodesResponse>, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/recoveryCodes");
    }

    public override async Task<Results<Ok<RecoveryCodesResponse>, ForbidHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

        if (email is null)
        {
            return TypedResults.Forbid();
        }

        var user = await db.Users.FirstOrDefaultAsync(e => e.Email == email.Value, ct);

        if (user is null)
        {
            return TypedResults.Forbid();
        }

        await db.RecoveryCodes
            .Where(e => !e.WasUsed && e.User == user)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasUsed, true), ct);

        var recoveryCodes = UserUtils.GenerateRecoveryCodes().ToList();

        foreach (var recoveryCode in recoveryCodes)
        {
            var code = new RecoveryCode
            {
                Id = Ulid.NewUlid(),
                Code = recoveryCode,
                User = user
            };

            await db.RecoveryCodes.AddAsync(code, ct);
        }

        return TypedResults.Ok(new RecoveryCodesResponse
        {
            Codes = recoveryCodes,
        });
    }
}