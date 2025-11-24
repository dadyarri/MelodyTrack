using FastEndpoints;
using MelodyTrack.Backend.Utils;
using MelodyTrack.Common.Api.Auth.Requests;
using MelodyTrack.Common.Api.Auth.Responses;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Data;
using MelodyTrack.Common.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class RefreshEndpoint(AppDbContext db)
    : Ep.Req<RefreshRequest>.Res<IResult>
{
    public override void Configure()
    {
        Post("/auth/refresh");
        AllowAnonymous();
    }

    public override async Task<IResult> ExecuteAsync(RefreshRequest req,
        CancellationToken ct)
    {
        Logger.LogDebug("Attempting to refresh token");

        var session = await db.Sessions
            .Where(e => e.RefreshToken == req.RefreshToken && !e.WasRevoked)
            .Include(e => e.User)
            .FirstOrDefaultAsync(ct);


        if (session is null)
        {
            Logger.LogWarning("Invalid or revoked refresh token used in refresh attempt");
            return ApiResults.Unauthorized();
        }

        await db.Sessions.Where(e => e.RefreshToken == req.RefreshToken)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);

        var refreshToken = UserUtils.GenerateRandomString(14);

        var newSession = new Session
        {
            Id = Ulid.NewUlid(),
            User = session.User,
            RefreshToken = refreshToken,
            DeviceInfo = BrowserUtils.GetDeviceInfo(HttpContext.Request.Headers.UserAgent),
            ValidUntil = DateTime.UtcNow.AddDays(7)
        };

        await db.Sessions.AddAsync(newSession, ct);
        await db.SaveChangesAsync(ct);

        Logger.LogInformation("Successfully refreshed token for user {Email} from {DeviceInfo}", session.User.Email, newSession.DeviceInfo);

        var response = new LoginResponse
        {
            AccessToken = UserUtils.CreateAccessToken(session.User),
            RefreshToken = refreshToken,
            FirstName = session.User.FirstName,
            LastName = session.User.LastName
        };

        return ApiResults.Ok(response);
    }
}