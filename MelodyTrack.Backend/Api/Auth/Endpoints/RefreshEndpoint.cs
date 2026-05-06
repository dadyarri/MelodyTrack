using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using UaDetector;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class RefreshEndpoint(AppDbContext db, IUaDetector uaDetector)
    : Ep.Req<RefreshRequest>.Res<Results<Ok<LoginResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/refresh");
        AllowAnonymous();
    }

    public override async Task<Results<Ok<LoginResponse>, UnauthorizedHttpResult>> ExecuteAsync(RefreshRequest req,
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
            return TypedResults.Unauthorized();
        }

        await db.Sessions.Where(e => e.RefreshToken == req.RefreshToken)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);

        var refreshToken = UserUtils.GenerateRandomString(14);

        var newSession = new Session
        {
            Id = Ulid.NewUlid(),
            User = session.User,
            RefreshToken = refreshToken,
            DeviceInfo = BrowserUtils.GetDeviceInfo(HttpContext.Request.Headers, uaDetector),
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

        return TypedResults.Ok(response);
    }
}
