using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;
using UaDetector;
using MelodyTrack.Data.Security;

namespace MelodyTrack.Backend.Api.ClientPortal;

public sealed class ClientPortalSessionService(
    AppDbContext db,
    IUaDetector uaDetector,
    RefreshSessionCookieService refreshCookieService,
    AuthenticationTokenHasher tokenHasher,
    JwtTokenService jwtTokenService,
    TimeProvider timeProvider,
    IHttpContextAccessor httpContextAccessor)
{
    public async Task<string> IssueAsync(User user, CancellationToken ct)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("An active HTTP context is required to create a portal session.");
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var refreshToken = UserUtils.GenerateRandomString(32);
        var session = new Session
        {
            Id = Ulid.NewUlid(),
            User = user,
            RefreshToken = tokenHasher.HashRefreshToken(refreshToken),
            DeviceInfo = BrowserUtils.GetDeviceInfo(httpContext.Request.Headers, uaDetector),
            ValidUntil = nowUtc.AddDays(30)
        };

        await db.Sessions.AddAsync(session, ct);
        refreshCookieService.Issue(httpContext.Response, refreshToken, session.ValidUntil);
        return jwtTokenService.CreateAccessToken(user, session.Id, timeProvider);
    }
}
