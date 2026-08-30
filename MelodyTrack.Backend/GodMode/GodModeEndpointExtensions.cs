using System.Diagnostics;
using System.Security.Cryptography;
using MelodyTrack.Backend.Configuration;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using MelodyTrack.Core.Auditing;
using MelodyTrack.Core.Configuration;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.GodMode;

public static class GodModeEndpointExtensions
{
    private const string RequestHeader = "X-God-Mode-Request";
    private const string SessionItemKey = "MelodyTrack.GodMode.Session";

    public static IApplicationBuilder UseGodModeListenerIsolation(
        this IApplicationBuilder app,
        GodModeOptions options)
    {
        return app.Use(async (context, next) =>
        {
            var isGodModePath = context.Request.Path.StartsWithSegments("/god-mode");
            var isGodModeListener = context.Connection.LocalPort == options.Port;
            if (isGodModeListener != isGodModePath)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (isGodModePath)
            {
                context.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
                context.Response.Headers.Pragma = "no-cache";
            }

            await next();
        });
    }

    public static void MapGodModeEndpoints(this WebApplication app)
    {
        var root = app.MapGroup("/god-mode").ExcludeFromDescription();
        root.MapGet("/", (HttpContext context) =>
        {
            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
            context.Response.Headers["Content-Security-Policy"] =
                $"default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; script-src 'nonce-{nonce}'; style-src 'unsafe-inline'; connect-src 'self'";
            context.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
            return Results.Content(GodModeHtml.Page.Replace("__NONCE__", nonce, StringComparison.Ordinal), "text/html; charset=utf-8");
        });
        root.MapPost("/session", ExchangeSessionAsync);

        var api = root.MapGroup("/api");
        api.AddEndpointFilter<GodModeAuthorizationFilter>();
        api.MapDelete("/session", EndSession);
        api.MapGet("/state", GetStateAsync);
        api.MapPost("/users/{id}/password-reset-requirements", RequirePasswordResetAsync);
        api.MapPost("/users/{id}/password-reset-links", CreatePasswordResetLinkAsync);
        api.MapDelete("/users/{id}/password-reset-links", RevokePasswordResetLinksAsync);
        api.MapDelete("/sessions/{id}", RevokeSessionAsync);
        api.MapDelete("/users/{id}/sessions", RevokeAllSessionsAsync);
        api.MapPost("/clients/{id}/portal-pin-resets", ResetPortalPinAsync);
        api.MapPost("/clients/{id}/portal-links", RotatePortalLinkAsync);
        api.MapDelete("/clients/{id}/portal-links", RevokePortalLinkAsync);
        api.MapPost("/notices", CreateNoticeAsync);
        api.MapPut("/notices/{id}", UpdateNoticeAsync);
        api.MapPost("/notices/{id}/expiration", ExpireNoticeAsync);
        api.MapDelete("/notices/{id}", DeleteNoticeAsync);
    }

    private static async Task<IResult> ExchangeSessionAsync(
        GodModeTokenRequest request,
        GodModeAccessService accessService,
        IAuditLogService auditLogService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var session = await accessService.ExchangeAsync(request.Token, ct);
        if (session is null)
        {
            return Results.Unauthorized();
        }

        httpContext.Response.Cookies.Append(
            GodModeAccessService.CookieName,
            accessService.CreateSessionCookie(session),
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/god-mode",
                Expires = new DateTimeOffset(session.ExpiresAtUtc),
                IsEssential = true
            });
        await WriteGodModeAuditAsync(
            auditLogService,
            AuditCatalog.Events.GodModeLoginSucceeded,
            session,
            "god_mode_session",
            session.Id,
            "success",
            httpContext,
            ct);
        return Results.NoContent();
    }

    private static async Task<IResult> EndSession(
        IAuditLogService auditLogService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var session = GetGodModeSession(httpContext);
        await WriteGodModeAuditAsync(
            auditLogService,
            AuditCatalog.Events.GodModeLogoutSucceeded,
            session,
            "god_mode_session",
            session.Id,
            "success",
            httpContext,
            ct);
        httpContext.Response.Cookies.Delete(
            GodModeAccessService.CookieName,
            new CookieOptions { Secure = true, SameSite = SameSiteMode.Strict, Path = "/god-mode" });
        return Results.NoContent();
    }

    private static async Task<IResult> GetStateAsync(
        AppDbContext db,
        IAuditLogService auditLogService,
        IPublicUrlBuilder publicUrlBuilder,
        TimeProvider timeProvider,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var userRows = await db.Users
            .AsNoTracking()
            .Include(user => user.Role)
            .OrderBy(user => user.LastName)
            .ThenBy(user => user.FirstName)
            .Select(user => new
            {
                user.Id,
                user.Email,
                DisplayName = user.LastName + " " + user.FirstName,
                Role = user.Role.DisplayName,
                user.PasswordResetRequired,
                TwoFactorEnabled = user.TotpSecret != null,
                user.ClientId,
                PortalLinkActive = user.ClientId != null && db.ClientPortalLoginLinks.Any(link => link.UserId == user.Id && link.RevokedAtUtc == null)
            })
            .ToListAsync(ct);
        var activeSessionCounts = await db.Sessions
            .AsNoTracking()
            .Where(session => !session.WasRevoked && session.ValidUntil > nowUtc)
            .GroupBy(session => session.User.Id)
            .Select(group => new { UserId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.UserId, item => item.Count, ct);
        var activeResetLinkCounts = await db.PasswordRestorationRequests
            .AsNoTracking()
            .Where(reset => !reset.WasUsed && reset.ValidUntil > nowUtc)
            .GroupBy(reset => reset.Email)
            .Select(group => new { Email = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Email, item => item.Count, StringComparer.OrdinalIgnoreCase, ct);
        var users = userRows.Select(user => new GodModeUserState(
            user.Id,
            user.Email,
            user.DisplayName,
            user.Role,
            user.PasswordResetRequired,
            user.TwoFactorEnabled,
            user.ClientId,
            user.PortalLinkActive,
            activeSessionCounts.GetValueOrDefault(user.Id),
            activeResetLinkCounts.GetValueOrDefault(user.Email))).ToArray();
        var noticeRecipients = users
            .Where(user => user.ClientId is null)
            .Select(user => new GodModeNoticeRecipientState("user", user.Id, user.DisplayName, user.Email, user.Role))
            .ToList();
        var clientNoticeRecipients = await db.Clients
            .AsNoTracking()
            .OrderBy(client => client.LastName)
            .ThenBy(client => client.FirstName)
            .Select(client => new GodModeNoticeRecipientState(
                "client",
                client.Id,
                client.LastName + " " + client.FirstName + (client.Patronymic == null ? "" : " " + client.Patronymic),
                client.Contacts.Email,
                "Клиент"))
            .ToListAsync(ct);
        noticeRecipients.AddRange(clientNoticeRecipients);
        var sessions = await db.Sessions
            .AsNoTracking()
            .Include(session => session.User)
            .Where(session => !session.WasRevoked && session.ValidUntil > nowUtc)
            .OrderByDescending(session => session.ValidUntil)
            .Select(session => new GodModeSessionState(
                session.Id,
                session.User.Id,
                session.User.Email,
                session.DeviceInfo,
                session.ValidUntil))
            .ToListAsync(ct);
        var inviteRows = await db.InviteCodes
            .AsNoTracking()
            .Include(invite => invite.Role)
            .OrderByDescending(invite => invite.ValidUntil)
            .Select(invite => new { invite.Id, invite.Email, Role = invite.Role.DisplayName, invite.ValidUntil, invite.WasUsed, invite.Code })
            .ToListAsync(ct);
        var invites = inviteRows.Select(invite => new GodModeInviteState(
            invite.Id,
            invite.Email,
            invite.Role,
            invite.ValidUntil,
            invite.WasUsed,
            invite.WasUsed || invite.ValidUntil <= nowUtc ? null : publicUrlBuilder.GetInviteUrl(invite.Code))).ToArray();
        var notices = await db.SystemNotices
            .AsNoTracking()
            .Include(notice => notice.Recipients)
            .OrderByDescending(notice => notice.CreatedAtUtc)
            .ToListAsync(ct);
        var noticeStates = notices.Select(notice => new GodModeNoticeState(
                notice.Id,
                notice.Title,
                notice.Body,
                notice.Severity.ToString().ToLowerInvariant(),
                notice.AudienceType.ToString().ToLowerInvariant(),
                notice.Dismissible,
                notice.ShowBeforeAuthentication,
                notice.CreatedAtUtc,
                notice.ExpiresAtUtc,
                notice.Recipients.Where(recipient => recipient.UserId != null).Select(recipient => recipient.UserId!.Value).ToArray(),
                notice.Recipients.Where(recipient => recipient.ClientId != null).Select(recipient => recipient.ClientId!.Value).ToArray()))
            .ToArray();

        await WriteGodModeAuditAsync(
            auditLogService,
            AuditCatalog.Events.GodModeStateInspected,
            GetGodModeSession(httpContext),
            "system",
            null,
            "success",
            httpContext,
            ct);
        return Results.Ok(new GodModeState(users, noticeRecipients, sessions, invites, noticeStates));
    }

    private static async Task<IResult> RequirePasswordResetAsync(
        Ulid id,
        AppDbContext db,
        IAuditLogService auditLogService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (user is null)
        {
            await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.GodModePasswordResetRequired, GetGodModeSession(httpContext), "user", id.ToString(), "not_found", httpContext, ct);
            return Results.NotFound();
        }

        user.PasswordResetRequired = true;
        await db.Sessions.Where(session => session.User.Id == id && !session.WasRevoked)
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.WasRevoked, true), ct);
        await db.SaveChangesAsync(ct);
        await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.GodModePasswordResetRequired, GetGodModeSession(httpContext), "user", id.ToString(), "success", httpContext, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CreatePasswordResetLinkAsync(
        Ulid id,
        AppDbContext db,
        IAuditLogService auditLogService,
        IPublicUrlBuilder publicUrlBuilder,
        TimeProvider timeProvider,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (user is null)
        {
            await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.GodModePasswordResetLinkCreated, GetGodModeSession(httpContext), "user", id.ToString(), "not_found", httpContext, ct);
            return Results.NotFound();
        }

        await db.PasswordRestorationRequests
            .Where(request => request.Email == user.Email && !request.WasUsed)
            .ExecuteUpdateAsync(setters => setters.SetProperty(request => request.WasUsed, true), ct);
        var token = UserUtils.GenerateRandomString(32);
        var restorationRequest = new PasswordRestorationRequest
        {
            Id = Ulid.NewUlid(),
            Email = user.Email,
            Token = UserUtils.HashOpaqueToken(token),
            ValidUntil = timeProvider.GetUtcNow().UtcDateTime.AddHours(2)
        };
        await db.PasswordRestorationRequests.AddAsync(restorationRequest, ct);
        await db.SaveChangesAsync(ct);
        await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.GodModePasswordResetLinkCreated, GetGodModeSession(httpContext), "user", id.ToString(), "success", httpContext, ct);
        return Results.Ok(new SecretUrlResponse(publicUrlBuilder.GetResetPasswordUrl(token)));
    }

    private static async Task<IResult> RevokePasswordResetLinksAsync(
        Ulid id,
        AppDbContext db,
        IAuditLogService auditLogService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, ct);
        if (user is null)
        {
            await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.GodModePasswordResetLinksRevoked, GetGodModeSession(httpContext), "user", id.ToString(), "not_found", httpContext, ct);
            return Results.NotFound();
        }

        await db.PasswordRestorationRequests
            .Where(request => request.Email == user.Email && !request.WasUsed)
            .ExecuteUpdateAsync(setters => setters.SetProperty(request => request.WasUsed, true), ct);
        await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.GodModePasswordResetLinksRevoked, GetGodModeSession(httpContext), "user", id.ToString(), "success", httpContext, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RevokeSessionAsync(
        Ulid id,
        AppDbContext db,
        IAuditLogService auditLogService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var updated = await db.Sessions.Where(session => session.Id == id && !session.WasRevoked)
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.WasRevoked, true), ct);
        if (updated == 0)
        {
            await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.GodModeSessionRevoked, GetGodModeSession(httpContext), "session", id.ToString(), "not_found", httpContext, ct);
            return Results.NotFound();
        }

        await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.GodModeSessionRevoked, GetGodModeSession(httpContext), "session", id.ToString(), "success", httpContext, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RevokeAllSessionsAsync(
        Ulid id,
        AppDbContext db,
        IAuditLogService auditLogService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!await db.Users.AsNoTracking().AnyAsync(user => user.Id == id, ct))
        {
            await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.GodModeAllSessionsRevoked, GetGodModeSession(httpContext), "user", id.ToString(), "not_found", httpContext, ct);
            return Results.NotFound();
        }

        await db.Sessions.Where(session => session.User.Id == id && !session.WasRevoked)
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.WasRevoked, true), ct);
        await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.GodModeAllSessionsRevoked, GetGodModeSession(httpContext), "user", id.ToString(), "success", httpContext, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ResetPortalPinAsync(
        Ulid id,
        AppDbContext db,
        IAuditLogService auditLogService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var link = await db.ClientPortalLoginLinks.Include(item => item.User)
            .FirstOrDefaultAsync(item => item.User.ClientId == id, ct);
        if (link is null)
        {
            await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.GodModePortalPinReset, GetGodModeSession(httpContext), "client", id.ToString(), "not_found", httpContext, ct);
            return Results.NotFound();
        }

        link.PinHash = null;
        link.PinSetAtUtc = null;
        link.FailedPinAttempts = 0;
        link.LastFailedPinAttemptAtUtc = null;
        await RevokePortalSessionsAsync(db, link, ct);
        await db.SaveChangesAsync(ct);
        await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.GodModePortalPinReset, GetGodModeSession(httpContext), "client", id.ToString(), "success", httpContext, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RotatePortalLinkAsync(
        Ulid id,
        AppDbContext db,
        IAuditLogService auditLogService,
        IPublicUrlBuilder publicUrlBuilder,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var link = await db.ClientPortalLoginLinks.Include(item => item.User)
            .FirstOrDefaultAsync(item => item.User.ClientId == id, ct);
        if (link is null)
        {
            await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.GodModePortalLinkRotated, GetGodModeSession(httpContext), "client", id.ToString(), "not_found", httpContext, ct);
            return Results.NotFound();
        }

        var token = UserUtils.GenerateRandomString(48);
        link.TokenHash = UserUtils.HashOpaqueToken(token);
        link.RevokedAtUtc = null;
        link.FailedPinAttempts = 0;
        link.LastFailedPinAttemptAtUtc = null;
        await RevokePortalSessionsAsync(db, link, ct);
        await db.SaveChangesAsync(ct);
        await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.GodModePortalLinkRotated, GetGodModeSession(httpContext), "client", id.ToString(), "success", httpContext, ct);
        return Results.Ok(new SecretUrlResponse(publicUrlBuilder.GetClientPortalAccessUrl(token)));
    }

    private static async Task<IResult> RevokePortalLinkAsync(
        Ulid id,
        AppDbContext db,
        IAuditLogService auditLogService,
        TimeProvider timeProvider,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var link = await db.ClientPortalLoginLinks.Include(item => item.User)
            .FirstOrDefaultAsync(item => item.User.ClientId == id, ct);
        if (link is null)
        {
            await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.GodModePortalLinkRevoked, GetGodModeSession(httpContext), "client", id.ToString(), "not_found", httpContext, ct);
            return Results.NotFound();
        }

        link.RevokedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        link.FailedPinAttempts = 0;
        link.LastFailedPinAttemptAtUtc = null;
        await RevokePortalSessionsAsync(db, link, ct);
        await db.SaveChangesAsync(ct);
        await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.GodModePortalLinkRevoked, GetGodModeSession(httpContext), "client", id.ToString(), "success", httpContext, ct);
        return Results.NoContent();
    }

    private static async Task RevokePortalSessionsAsync(AppDbContext db, ClientPortalLoginLink link, CancellationToken ct)
    {
        await db.ClientPortalSavedIdentityReferences.Where(item => item.LoginLinkId == link.Id).ExecuteDeleteAsync(ct);
        await db.Sessions.Where(item => item.User.Id == link.User.Id && !item.WasRevoked)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.WasRevoked, true), ct);
    }

    private static async Task<IResult> CreateNoticeAsync(
        GodModeNoticeRequest request,
        AppDbContext db,
        IAuditLogService auditLogService,
        TimeProvider timeProvider,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var parsed = ParseNotice(request, timeProvider.GetUtcNow().UtcDateTime);
        if (parsed.Error is not null)
        {
            await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.SystemNoticeCreated, GetGodModeSession(httpContext), "system_notice", null, "validation_failed", httpContext, ct);
            return Results.BadRequest(new { error = parsed.Error });
        }

        var notice = parsed.Notice!;
        await db.SystemNotices.AddAsync(notice, ct);
        await SetRecipientsAsync(db, notice, request.UserIds, request.ClientIds, ct);
        await db.SaveChangesAsync(ct);
        await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.SystemNoticeCreated, GetGodModeSession(httpContext), "system_notice", notice.Id.ToString(), "success", httpContext, ct);
        return Results.Created($"/god-mode/api/notices/{notice.Id}", new { notice.Id });
    }

    private static async Task<IResult> UpdateNoticeAsync(
        Ulid id,
        GodModeNoticeRequest request,
        AppDbContext db,
        IAuditLogService auditLogService,
        TimeProvider timeProvider,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var notice = await db.SystemNotices.Include(item => item.Recipients).FirstOrDefaultAsync(item => item.Id == id, ct);
        if (notice is null)
        {
            await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.SystemNoticeUpdated, GetGodModeSession(httpContext), "system_notice", id.ToString(), "not_found", httpContext, ct);
            return Results.NotFound();
        }

        var parsed = ParseNotice(request, notice.CreatedAtUtc);
        if (parsed.Error is not null)
        {
            await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.SystemNoticeUpdated, GetGodModeSession(httpContext), "system_notice", id.ToString(), "validation_failed", httpContext, ct);
            return Results.BadRequest(new { error = parsed.Error });
        }

        var values = parsed.Notice!;
        notice.Title = values.Title;
        notice.Body = values.Body;
        notice.Severity = values.Severity;
        notice.AudienceType = values.AudienceType;
        notice.Dismissible = values.Dismissible;
        notice.ShowBeforeAuthentication = values.ShowBeforeAuthentication;
        notice.ExpiresAtUtc = values.ExpiresAtUtc;
        notice.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        db.SystemNoticeRecipients.RemoveRange(notice.Recipients);
        await SetRecipientsAsync(db, notice, request.UserIds, request.ClientIds, ct);
        await db.SaveChangesAsync(ct);
        await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.SystemNoticeUpdated, GetGodModeSession(httpContext), "system_notice", id.ToString(), "success", httpContext, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ExpireNoticeAsync(
        Ulid id,
        AppDbContext db,
        IAuditLogService auditLogService,
        TimeProvider timeProvider,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var updated = await db.SystemNotices.Where(notice => notice.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(notice => notice.ExpiresAtUtc, nowUtc)
                .SetProperty(notice => notice.UpdatedAtUtc, nowUtc), ct);
        if (updated == 0)
        {
            await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.SystemNoticeExpired, GetGodModeSession(httpContext), "system_notice", id.ToString(), "not_found", httpContext, ct);
            return Results.NotFound();
        }

        await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.SystemNoticeExpired, GetGodModeSession(httpContext), "system_notice", id.ToString(), "success", httpContext, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteNoticeAsync(
        Ulid id,
        AppDbContext db,
        IAuditLogService auditLogService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var deleted = await db.SystemNotices.Where(notice => notice.Id == id).ExecuteDeleteAsync(ct);
        if (deleted == 0)
        {
            await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.SystemNoticeDeleted, GetGodModeSession(httpContext), "system_notice", id.ToString(), "not_found", httpContext, ct);
            return Results.NotFound();
        }

        await WriteGodModeAuditAsync(auditLogService, AuditCatalog.Events.SystemNoticeDeleted, GetGodModeSession(httpContext), "system_notice", id.ToString(), "success", httpContext, ct);
        return Results.NoContent();
    }

    private static (SystemNotice? Notice, string? Error) ParseNotice(GodModeNoticeRequest request, DateTime createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200)
        {
            return (null, "Заголовок обязателен и не должен превышать 200 символов.");
        }

        var body = request.Body?.Trim() ?? string.Empty;
        if (body.Length > 4000)
        {
            return (null, "Текст уведомления не должен превышать 4000 символов.");
        }

        if (!Enum.TryParse<SystemNoticeSeverity>(request.Severity, true, out var severity) ||
            !Enum.TryParse<SystemNoticeAudienceType>(request.AudienceType, true, out var audience))
        {
            return (null, "Неизвестная важность или аудитория уведомления.");
        }

        if (request.ShowBeforeAuthentication && audience != SystemNoticeAudienceType.Everyone)
        {
            return (null, "Публичное уведомление до входа может быть адресовано только всем.");
        }

        if (audience == SystemNoticeAudienceType.SpecificRecipients && request.UserIds.Count == 0 && request.ClientIds.Count == 0)
        {
            return (null, "Для адресного уведомления нужен хотя бы один получатель.");
        }

        return (new SystemNotice
        {
            Id = Ulid.NewUlid(),
            Title = request.Title.Trim(),
            Body = body,
            Severity = severity,
            AudienceType = audience,
            Dismissible = request.Dismissible,
            ShowBeforeAuthentication = request.ShowBeforeAuthentication,
            CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = request.ExpiresAtUtc is { } expiresAtUtc
                ? expiresAtUtc.Kind switch
                {
                    DateTimeKind.Utc => expiresAtUtc,
                    DateTimeKind.Local => expiresAtUtc.ToUniversalTime(),
                    _ => DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc)
                }
                : null
        }, null);
    }

    private static async Task SetRecipientsAsync(
        AppDbContext db,
        SystemNotice notice,
        IReadOnlyCollection<Ulid> userIds,
        IReadOnlyCollection<Ulid> clientIds,
        CancellationToken ct)
    {
        if (notice.AudienceType != SystemNoticeAudienceType.SpecificRecipients)
        {
            return;
        }

        var validUserIds = await db.Users.Where(user => userIds.Contains(user.Id)).Select(user => user.Id).ToListAsync(ct);
        var validClientIds = await db.Clients.Where(client => clientIds.Contains(client.Id)).Select(client => client.Id).ToListAsync(ct);
        foreach (var userId in validUserIds.Distinct())
        {
            notice.Recipients.Add(new SystemNoticeRecipient
            {
                Id = Ulid.NewUlid(),
                Notice = notice,
                NoticeId = notice.Id,
                UserId = userId
            });
        }

        foreach (var clientId in validClientIds.Distinct())
        {
            notice.Recipients.Add(new SystemNoticeRecipient
            {
                Id = Ulid.NewUlid(),
                Notice = notice,
                NoticeId = notice.Id,
                ClientId = clientId
            });
        }
    }

    private static GodModeSession GetGodModeSession(HttpContext context) =>
        (GodModeSession)context.Items[SessionItemKey]!;

    private static Task WriteGodModeAuditAsync(
        IAuditLogService auditLogService,
        AuditEventDefinition auditEvent,
        GodModeSession session,
        string entityType,
        string? entityId,
        string result,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;
        return auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Event = auditEvent,
            EntityType = entityType,
            EntityId = entityId,
            ActorEmail = $"god-mode:{session.Id}",
            ActorDisplayName = "God mode",
            Details = $"Сессия god mode: {session.Id}; результат: {result}; trace ID: {traceId}"
        }, ct);
    }

    private sealed class GodModeAuthorizationFilter(
        GodModeAccessService accessService) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocationContext, EndpointFilterDelegate next)
        {
            var context = invocationContext.HttpContext;
            var session = accessService.ValidateSessionCookie(context.Request.Cookies[GodModeAccessService.CookieName]);
            if (session is null)
            {
                return Results.Unauthorized();
            }

            if (!HttpMethods.IsGet(context.Request.Method) && context.Request.Headers[RequestHeader] != "1")
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            context.Items[SessionItemKey] = session;
            return await next(invocationContext);
        }
    }
}

public sealed record GodModeTokenRequest(string Token);
public sealed record SecretUrlResponse(string Url);
public sealed record GodModeState(
    IReadOnlyList<GodModeUserState> Users,
    IReadOnlyList<GodModeNoticeRecipientState> NoticeRecipients,
    IReadOnlyList<GodModeSessionState> Sessions,
    IReadOnlyList<GodModeInviteState> Invites,
    IReadOnlyList<GodModeNoticeState> Notices);
public sealed record GodModeUserState(
    Ulid Id,
    string Email,
    string DisplayName,
    string Role,
    bool PasswordResetRequired,
    bool TwoFactorEnabled,
    Ulid? ClientId,
    bool PortalLinkActive,
    int ActiveSessionCount,
    int ActivePasswordResetLinkCount);
public sealed record GodModeNoticeRecipientState(
    string Type,
    Ulid Id,
    string DisplayName,
    string? Email,
    string Detail);
public sealed record GodModeSessionState(Ulid Id, Ulid UserId, string Email, string DeviceInfo, DateTime ValidUntilUtc);
public sealed record GodModeInviteState(Ulid Id, string? Email, string Role, DateTime ValidUntilUtc, bool WasUsed, string? Url);
public sealed record GodModeNoticeState(
    Ulid Id,
    string Title,
    string Body,
    string Severity,
    string AudienceType,
    bool Dismissible,
    bool ShowBeforeAuthentication,
    DateTime CreatedAtUtc,
    DateTime? ExpiresAtUtc,
    IReadOnlyList<Ulid> UserIds,
    IReadOnlyList<Ulid> ClientIds);
public sealed class GodModeNoticeRequest
{
    public required string Title { get; init; }
    public string? Body { get; init; }
    public required string Severity { get; init; }
    public required string AudienceType { get; init; }
    public bool Dismissible { get; init; }
    public bool ShowBeforeAuthentication { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public IReadOnlyCollection<Ulid> UserIds { get; init; } = [];
    public IReadOnlyCollection<Ulid> ClientIds { get; init; } = [];
}

internal static class GodModeHtml
{
    public const string Page = """
<!doctype html>
<html lang="ru">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="referrer" content="no-referrer">
  <title>MelodyTrack — аварийный доступ</title>
  <style>
    :root{color-scheme:dark;font:16px system-ui;background:#10131a;color:#edf1f7}body{max-width:1180px;margin:auto;padding:24px}button,input,textarea,select{font:inherit}button{padding:8px 12px;cursor:pointer}.card{background:#181d27;border:1px solid #343b4a;border-radius:12px;padding:16px;margin:12px 0}.row{display:flex;gap:8px;flex-wrap:wrap;align-items:center}.field{display:grid;gap:6px}.grow{flex:1 1 260px}.muted{color:#aeb7c6}table{width:100%;border-collapse:collapse}th,td{text-align:left;padding:8px;border-bottom:1px solid #343b4a;vertical-align:top}textarea,input,select{background:#10131a;color:#edf1f7;border:1px solid #4b5568;border-radius:6px;padding:8px}textarea{width:100%;box-sizing:border-box;min-height:96px}.notice-form{display:grid;gap:12px}.recipient-picker{background:#10131a;border:1px solid #343b4a;border-radius:8px;padding:12px}.recipient-list{display:grid;grid-template-columns:1fr;gap:8px;margin-top:12px;max-height:320px;overflow:auto}.recipient{display:flex;gap:10px;align-items:flex-start;border:1px solid #343b4a;border-radius:8px;padding:10px;cursor:pointer}.recipient:hover{border-color:#71809d}.recipient input{margin-top:3px}.recipient-copy{display:grid;gap:2px;min-width:0}.recipient-copy span{overflow-wrap:anywhere}.secret{word-break:break-all;color:#ffcf70}.critical{border-color:#b94b55}@media(max-width:720px){body{padding:12px}table{display:block;overflow:auto}}
  </style>
</head>
<body>
  <h1>Аварийный доступ MelodyTrack</h1>
  <section id="login" class="card"><p>Откройте одноразовую ссылку, созданную серверной командой <code>melodytrack god-mode</code>.</p></section>
  <main id="app" hidden>
    <div class="row"><button data-command="refresh">Обновить</button><button data-command="logout">Завершить сессию</button><span id="status" class="muted"></span></div>
    <section class="card"><h2>Пользователи и учетные данные</h2><div id="users"></div></section>
    <section class="card"><h2>Активные сессии</h2><div id="sessions"></div></section>
    <section class="card"><h2>Bootstrap и приглашения</h2><div id="invites"></div></section>
    <section class="card"><h2>Системные уведомления</h2>
      <div class="notice-form" id="noticeForm">
        <div class="row">
          <label class="field grow"><span>Заголовок</span><input id="noticeTitle" maxlength="200" placeholder="Что произошло"></label>
          <label class="field"><span>Важность</span><select id="noticeSeverity"><option value="information">Информация</option><option value="success">Успех</option><option value="warning">Предупреждение</option><option value="critical">Критично</option></select></label>
          <label class="field"><span>Аудитория</span><select id="noticeAudience"><option value="everyone">Все</option><option value="staff">Сотрудники</option><option value="clients">Клиенты</option><option value="specificRecipients">Конкретные получатели</option></select></label>
          <label class="field"><span>Показывать до</span><input type="datetime-local" id="noticeExpires"></label>
        </div>
        <label class="field"><span>Текст <span class="muted">(необязательно)</span></span><textarea id="noticeBody" maxlength="4000" placeholder="Дополнительные сведения без чувствительных данных"></textarea></label>
        <div id="noticeRecipients" class="recipient-picker" hidden>
          <div class="row">
            <label class="field grow"><span>Получатели</span><input type="search" id="noticeRecipientSearch" placeholder="Найти по имени, почте или роли"></label>
            <span id="noticeRecipientSummary" class="muted"></span>
          </div>
          <div id="noticeRecipientList" class="recipient-list"></div>
        </div>
        <div class="row"><label><input type="checkbox" id="noticeDismissible" checked> можно закрыть</label><label><input type="checkbox" id="noticePreAuth"> показать до входа</label><button id="noticeSubmit" data-command="save-notice">Создать</button><button id="noticeCancel" data-command="cancel-notice" hidden>Отменить</button><span id="noticeFormStatus" class="muted"></span></div>
      </div>
      <div id="notices"></div>
    </section>
  </main>
<script nonce="__NONCE__">
const headers={'Content-Type':'application/json','X-God-Mode-Request':'1'};
let currentState=null;
let editingNoticeId=null;
const selectedNoticeUserIds=new Set();
const selectedNoticeClientIds=new Set();
const noticeForm={title:document.querySelector('#noticeTitle'),body:document.querySelector('#noticeBody'),severity:document.querySelector('#noticeSeverity'),audience:document.querySelector('#noticeAudience'),expires:document.querySelector('#noticeExpires'),dismissible:document.querySelector('#noticeDismissible'),preAuth:document.querySelector('#noticePreAuth'),recipients:document.querySelector('#noticeRecipients'),recipientSearch:document.querySelector('#noticeRecipientSearch'),recipientList:document.querySelector('#noticeRecipientList'),recipientSummary:document.querySelector('#noticeRecipientSummary'),submit:document.querySelector('#noticeSubmit'),cancel:document.querySelector('#noticeCancel'),status:document.querySelector('#noticeFormStatus')};
const esc=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
async function api(path,options={}){const response=await fetch('/god-mode/api'+path,{credentials:'same-origin',...options,headers:{...headers,...options.headers}});if(response.status===401){showLogin();throw new Error('Сессия закончилась');}if(!response.ok)throw new Error(await response.text()||response.statusText);return response.status===204?null:response.json()}
function showLogin(){document.querySelector('#login').hidden=false;document.querySelector('#app').hidden=true}
function showApp(){document.querySelector('#login').hidden=true;document.querySelector('#app').hidden=false}
async function exchange(){const token=location.hash.startsWith('#token=')?decodeURIComponent(location.hash.slice(7)):null;history.replaceState(null,'',location.pathname);if(token){const response=await fetch('/god-mode/session',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({token})});if(!response.ok){document.querySelector('#login').innerHTML='<p>Ссылка недействительна, истекла или уже использована.</p>';return}}try{await loadState();showApp()}catch{showLogin()}}
const severityLabels={information:'Информация',success:'Успех',warning:'Предупреждение',critical:'Критично'};
const audienceLabels={everyone:'Все',staff:'Сотрудники',clients:'Клиенты',specificRecipients:'Конкретные получатели'};
function recipientNames(notice){if(notice.audienceType!=='specificRecipients')return'';const names=(currentState?.noticeRecipients??[]).filter(recipient=>recipient.type==='user'?notice.userIds.includes(recipient.id):notice.clientIds.includes(recipient.id)).map(recipient=>recipient.displayName);return names.length?names.join(', '):'Получатели недоступны'}
function renderRecipientPicker(){const visible=noticeForm.audience.value==='specificRecipients';noticeForm.recipients.hidden=!visible;noticeForm.preAuth.disabled=noticeForm.audience.value!=='everyone';if(noticeForm.preAuth.disabled)noticeForm.preAuth.checked=false;if(!visible)return;const query=noticeForm.recipientSearch.value.trim().toLocaleLowerCase('ru');const matches=(currentState?.noticeRecipients??[]).filter(recipient=>`${recipient.displayName} ${recipient.email??''} ${recipient.detail}`.toLocaleLowerCase('ru').includes(query));noticeForm.recipientList.innerHTML=matches.length?matches.map(recipient=>{const checked=recipient.type==='user'?selectedNoticeUserIds.has(recipient.id):selectedNoticeClientIds.has(recipient.id);return`<label class="recipient"><input type="checkbox" data-recipient-type="${recipient.type}" data-recipient-id="${recipient.id}" ${checked?'checked':''}><span class="recipient-copy"><strong>${esc(recipient.displayName)}</strong>${recipient.email?`<span>${esc(recipient.email)}</span>`:''}<span class="muted">${esc(recipient.detail)}</span></span></label>`}).join(''):'<p class="muted">Получатели не найдены.</p>';const count=selectedNoticeUserIds.size+selectedNoticeClientIds.size;noticeForm.recipientSummary.textContent=count?`Выбрано: ${count}`:'Никто не выбран'}
function resetNoticeForm(){editingNoticeId=null;selectedNoticeUserIds.clear();selectedNoticeClientIds.clear();noticeForm.title.value='';noticeForm.body.value='';noticeForm.severity.value='information';noticeForm.audience.value='everyone';noticeForm.expires.value='';noticeForm.dismissible.checked=true;noticeForm.preAuth.checked=false;noticeForm.recipientSearch.value='';noticeForm.submit.textContent='Создать';noticeForm.cancel.hidden=true;noticeForm.status.textContent='';renderRecipientPicker()}
function toLocalDateTime(value){if(!value)return'';const date=new Date(value);const local=new Date(date.getTime()-date.getTimezoneOffset()*60000);return local.toISOString().slice(0,16)}
async function loadState(){status.textContent='Загрузка…';const state=await api('/state');currentState=state;users.innerHTML='<table><tr><th>Пользователь</th><th>Состояние</th><th>Действия</th></tr>'+state.users.map(u=>`<tr><td>${esc(u.displayName)}<br><span class="muted">${esc(u.email)} · ${esc(u.role)}<br>user: ${esc(u.id)}${u.clientId?'<br>client: '+esc(u.clientId):''}</span></td><td>2FA: ${u.twoFactorEnabled?'да':'нет'}<br>Сброс обязателен: ${u.passwordResetRequired?'да':'нет'}<br>Сессий: ${u.activeSessionCount}; ссылок сброса: ${u.activePasswordResetLinkCount}</td><td><div class="row"><button data-command="act" data-method="POST" data-path="/users/${u.id}/password-reset-requirements">Обязать сбросить пароль</button><button data-command="secret" data-path="/users/${u.id}/password-reset-links">Создать ссылку сброса</button><button data-command="act" data-method="DELETE" data-path="/users/${u.id}/password-reset-links">Отозвать ссылки</button><button data-command="act" data-method="DELETE" data-path="/users/${u.id}/sessions">Завершить все сессии</button>${u.clientId?`<button data-command="act" data-method="POST" data-path="/clients/${u.clientId}/portal-pin-resets">Сбросить PIN</button><button data-command="secret" data-path="/clients/${u.clientId}/portal-links">Обновить ссылку кабинета</button><button data-command="act" data-method="DELETE" data-path="/clients/${u.clientId}/portal-links">Отозвать кабинет</button>`:''}</div></td></tr>`).join('')+'</table>';
sessions.innerHTML='<table><tr><th>Пользователь</th><th>Устройство / срок</th><th></th></tr>'+state.sessions.map(s=>`<tr><td>${esc(s.email)}</td><td>${esc(s.deviceInfo)}<br>${esc(s.validUntilUtc)}</td><td><button data-command="act" data-method="DELETE" data-path="/sessions/${s.id}">Завершить</button></td></tr>`).join('')+'</table>';
invites.innerHTML='<table><tr><th>Получатель</th><th>Роль / срок</th><th>Ссылка</th></tr>'+state.invites.map(i=>`<tr><td>${esc(i.email||'без привязки')}</td><td>${esc(i.role)}<br>${esc(i.validUntilUtc)}${i.wasUsed?' · использовано':''}</td><td class="secret">${i.url?esc(i.url):'—'}</td></tr>`).join('')+'</table>';
notices.innerHTML='<table><tr><th>Уведомление</th><th>Параметры</th><th></th></tr>'+state.notices.map(n=>`<tr class="${n.severity==='critical'?'critical':''}"><td><strong>${esc(n.title)}</strong>${n.body?'<br>'+esc(n.body):''}</td><td>${esc(severityLabels[n.severity]||n.severity)} · ${esc(audienceLabels[n.audienceType]||n.audienceType)}${n.audienceType==='specificRecipients'?'<br>'+esc(recipientNames(n)):''}<br>${n.expiresAtUtc?'до '+esc(n.expiresAtUtc):'без срока'}</td><td><button data-command="edit-notice" data-id="${n.id}">Изменить</button><button data-command="act" data-method="POST" data-path="/notices/${n.id}/expiration">Завершить</button><button data-command="act" data-method="DELETE" data-path="/notices/${n.id}">Удалить</button></td></tr>`).join('')+'</table>';renderRecipientPicker();status.textContent='Обновлено'}
async function act(path,method){if(!confirm('Подтвердить действие?'))return;try{await api(path,{method});await loadState()}catch(e){alert(e.message)}}
async function secret(path){if(!confirm('Создать новый одноразовый секрет?'))return;try{const value=await api(path,{method:'POST'});prompt('Скопируйте ссылку сейчас. Она больше не будет показана:',value.url);await loadState()}catch(e){alert(e.message)}}
async function saveNotice(){noticeForm.status.textContent='';if(!noticeForm.title.value.trim()){noticeForm.status.textContent='Заполните заголовок.';return}if(noticeForm.audience.value==='specificRecipients'&&selectedNoticeUserIds.size+selectedNoticeClientIds.size===0){noticeForm.status.textContent='Выберите хотя бы одного получателя.';return}const payload={title:noticeForm.title.value,body:noticeForm.body.value,severity:noticeForm.severity.value,audienceType:noticeForm.audience.value,dismissible:noticeForm.dismissible.checked,showBeforeAuthentication:noticeForm.preAuth.checked,expiresAtUtc:noticeForm.expires.value?new Date(noticeForm.expires.value).toISOString():null,userIds:[...selectedNoticeUserIds],clientIds:[...selectedNoticeClientIds]};noticeForm.submit.disabled=true;noticeForm.status.textContent='Сохранение…';try{await api(editingNoticeId?'/notices/'+editingNoticeId:'/notices',{method:editingNoticeId?'PUT':'POST',body:JSON.stringify(payload)});resetNoticeForm();await loadState()}catch(e){noticeForm.status.textContent=e.message}finally{noticeForm.submit.disabled=false}}
function editNotice(id){const notice=currentState?.notices.find(item=>item.id===id);if(!notice)return;editingNoticeId=id;selectedNoticeUserIds.clear();selectedNoticeClientIds.clear();notice.userIds.forEach(value=>selectedNoticeUserIds.add(value));notice.clientIds.forEach(value=>selectedNoticeClientIds.add(value));noticeForm.title.value=notice.title;noticeForm.body.value=notice.body;noticeForm.severity.value=notice.severity;noticeForm.audience.value=notice.audienceType;noticeForm.expires.value=toLocalDateTime(notice.expiresAtUtc);noticeForm.dismissible.checked=notice.dismissible;noticeForm.preAuth.checked=notice.showBeforeAuthentication;noticeForm.recipientSearch.value='';noticeForm.submit.textContent='Сохранить';noticeForm.cancel.hidden=false;noticeForm.status.textContent='Редактирование уведомления';renderRecipientPicker();document.querySelector('#noticeForm').scrollIntoView({behavior:'smooth',block:'start'});noticeForm.title.focus()}
async function logout(){await api('/session',{method:'DELETE'}).catch(()=>{});showLogin()}
noticeForm.audience.addEventListener('change',renderRecipientPicker);
noticeForm.recipientSearch.addEventListener('input',renderRecipientPicker);
noticeForm.recipientList.addEventListener('change',event=>{const checkbox=event.target.closest('input[data-recipient-type]');if(!checkbox)return;const selection=checkbox.dataset.recipientType==='user'?selectedNoticeUserIds:selectedNoticeClientIds;if(checkbox.checked)selection.add(checkbox.dataset.recipientId);else selection.delete(checkbox.dataset.recipientId);renderRecipientPicker()});
document.addEventListener('click',event=>{const button=event.target.closest('button[data-command]');if(!button)return;const command=button.dataset.command;if(command==='refresh')void loadState();else if(command==='logout')void logout();else if(command==='save-notice')void saveNotice();else if(command==='cancel-notice')resetNoticeForm();else if(command==='edit-notice')void editNotice(button.dataset.id);else if(command==='act')void act(button.dataset.path,button.dataset.method);else if(command==='secret')void secret(button.dataset.path)});
exchange();
</script>
</body>
</html>
""";
}
