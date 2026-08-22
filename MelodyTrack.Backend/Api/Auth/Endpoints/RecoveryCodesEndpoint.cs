using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/auth/recovery-codes")]
public sealed class RecoveryCodesEndpoint
{

    public static async Task<Results<Ok<RecoveryCodesResponse>, UnauthorizedHttpResult>> HandleAsync(
        AppDbContext db,
        IAuditLogService auditLogService,
        ICurrentUserAccessor currentUserAccessor,
        ILogger<RecoveryCodesEndpoint> logger,
        CancellationToken ct
    )
    {
        var user = await currentUserAccessor.GetAsync(ct);

        if (user is null)
        {
            logger.LogWarning("Recovery codes generation attempt without a current user");
            return TypedResults.Unauthorized();
        }

        logger.LogDebug("Invalidating existing unused recovery codes for {EmailRef}", UserUtils.DescribeEmailForLogs(user.Email));
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

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Successfully generated {Count} new recovery codes for {EmailRef}", recoveryCodes.Count, UserUtils.DescribeEmailForLogs(user.Email));
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "auth",
            Action = "recovery_codes_regenerated",
            EntityType = "user",
            EntityId = user.Id.ToString(),
            ActorUserId = user.Id,
            ActorEmail = user.Email,
            ActorDisplayName = $"{user.LastName} {user.FirstName}".Trim(),
            Details = $"Сгенерировано кодов восстановления: {recoveryCodes.Count}"
        }, ct);
        return TypedResults.Ok(new RecoveryCodesResponse
        {
            AllCodes = recoveryCodes.Select(code => new RecoveryCodeDto
            {
                Code = code,
                WasUsed = false
            }).ToList()
        });
    }
}
