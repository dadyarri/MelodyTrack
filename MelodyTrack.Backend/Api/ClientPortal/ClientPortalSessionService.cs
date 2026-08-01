using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;
using UaDetector;

namespace MelodyTrack.Backend.Api.ClientPortal;

public sealed class ClientPortalSessionService(
    AppDbContext db,
    IUaDetector uaDetector,
    RefreshSessionCookieService refreshCookieService,
    TimeProvider timeProvider,
    IHttpContextAccessor httpContextAccessor)
{
    public async Task<string> IssueAsync(User user, CancellationToken ct)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("An active HTTP context is required to create a portal session.");
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        await db.Sessions
            .Where(item => item.User.Id == user.Id && !item.WasRevoked)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.WasRevoked, true), ct);

        var refreshToken = UserUtils.GenerateRandomString(32);
        var session = new Session
        {
            Id = Ulid.NewUlid(),
            User = user,
            RefreshToken = UserUtils.HashOpaqueToken(refreshToken),
            DeviceInfo = BrowserUtils.GetDeviceInfo(httpContext.Request.Headers, uaDetector),
            ValidUntil = nowUtc.AddDays(30)
        };

        await db.Sessions.AddAsync(session, ct);
        refreshCookieService.Issue(httpContext.Response, refreshToken, session.ValidUntil);
        return UserUtils.CreateAccessToken(user, session.Id, timeProvider);
    }
}
