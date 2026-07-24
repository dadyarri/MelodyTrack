using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class GetRecoveryCodesEndpoint(AppDbContext db, ICurrentUserAccessor currentUserAccessor)
    : Ep.NoReq.Res<Results<Ok<RecoveryCodesResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Get("/auth/recoveryCodes");
    }

    public override async Task<Results<Ok<RecoveryCodesResponse>, UnauthorizedHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var user = await currentUserAccessor.GetAsync(ct);
        if (user is null)
        {
            Logger.LogWarning("Recovery codes list request without a current user");
            return TypedResults.Unauthorized();
        }

        var codes = await db.RecoveryCodes
            .Where(e => e.User.Id == user.Id)
            .Select(e => new RecoveryCodeDto
            {
                Code = e.Code,
                WasUsed = e.WasUsed
            })
            .OrderBy(e => e.WasUsed)
            .ThenBy(e => e.Code)
            .ToListAsync(ct);

        return TypedResults.Ok(new RecoveryCodesResponse
        {
            AllCodes = codes
        });
    }
}
