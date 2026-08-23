using System.Security.Cryptography;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Core.Configuration;
using MelodyTrack.Core.Security;
using MelodyTrack.Data.Security;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MelodyTrack.Data.Initialization;

public sealed class SuperuserRecoveryService(
    AppDbContext db,
    IPersonalDataProtector personalDataProtector,
    IOptions<PublicUrlOptions> publicUrlOptions,
    TimeProvider timeProvider)
{
    public async Task<SuperuserRecoveryResult> CreateResetUrlAsync(string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var emailBlindIndex = personalDataProtector.HashEmailBlindIndex(normalizedEmail);
        var user = await db.Users
            .Include(item => item.Role)
            .SingleOrDefaultAsync(item => item.EmailBlindIndex == emailBlindIndex, cancellationToken);
        if (user is null || !user.Role.RoleName.IsSuperuser())
        {
            throw new InvalidOperationException("The requested superuser account does not exist.");
        }

        await db.PasswordRestorationRequests
            .Where(request => request.Email == user.Email && !request.WasUsed)
            .ExecuteUpdateAsync(setters => setters.SetProperty(request => request.WasUsed, true), cancellationToken);

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var recoveryCode = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(12));
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        await db.PasswordRestorationRequests.AddAsync(new PasswordRestorationRequest
        {
            Id = Ulid.NewUlid(),
            Email = user.Email,
            Token = AuthenticationTokenHasher.HashOpaqueToken(token),
            ValidUntil = nowUtc.AddMinutes(30)
        }, cancellationToken);
        await db.RecoveryCodes.AddAsync(new RecoveryCode
        {
            Id = Ulid.NewUlid(),
            User = user,
            Code = recoveryCode
        }, cancellationToken);
        await db.AuditLogs.AddAsync(new AuditLog
        {
            Id = Ulid.NewUlid(),
            CreatedAtUtc = nowUtc,
            Category = "security",
            Action = "superuser_recovery_issued",
            EntityType = "user",
            EntityId = user.Id.ToString(),
            Details = "Серверный оператор выпустил одноразовую ссылку восстановления первого суперпользователя"
        }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new SuperuserRecoveryResult(
            $"{publicUrlOptions.Value.BaseUrl.TrimEnd('/')}/restore?code={Uri.EscapeDataString(token)}",
            recoveryCode);
    }
}

public sealed record SuperuserRecoveryResult(string ResetUrl, string RecoveryCode);
