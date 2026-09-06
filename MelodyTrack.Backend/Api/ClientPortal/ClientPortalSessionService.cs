using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using MelodyTrack.Data.Security;
using UaDetector;

namespace MelodyTrack.Backend.Api.ClientPortal;

public sealed class ClientPortalSessionService(
    AppDbContext db,
    IUaDetector uaDetector,
    RefreshSessionCookieService refreshCookieService,
    AuthenticationTokenHasher tokenHasher,
    TimeProvider timeProvider,
    IHttpContextAccessor httpContextAccessor)
{
    public async Task<(string RefreshToken, DateTime ValidUntil)> CreateAsync(User user, CancellationToken ct)
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
        return (refreshToken, session.ValidUntil);
    }

    public void IssueCookie(HttpResponse response, (string RefreshToken, DateTime ValidUntil) session)
    {
        refreshCookieService.Issue(response, session.RefreshToken, session.ValidUntil);
    }
}
