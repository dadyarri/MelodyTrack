using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/auth/recovery-codes")]
public sealed class GetRecoveryCodesEndpoint
{

    public static async Task<Results<Ok<RecoveryCodesResponse>, UnauthorizedHttpResult>> HandleAsync(
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        ILogger<GetRecoveryCodesEndpoint> logger,
        CancellationToken ct
    )
    {
        var user = await currentUserAccessor.GetAsync(ct);
        if (user is null)
        {
            logger.LogWarning("Recovery codes list request without a current user");
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
