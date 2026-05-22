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
            .Where(e => e.RefreshToken == req.RefreshToken)
            .Include(e => e.User)
            .FirstOrDefaultAsync(ct);


        if (session is null)
        {
            Logger.LogWarning("Unknown refresh token used in refresh attempt");
            return TypedResults.Unauthorized();
        }

        if (session.WasRevoked)
        {
            Logger.LogWarning(
                "Revoked refresh token replay detected for user {Email}. Revoking all sessions.",
                session.User.Email);
            await db.Sessions
                .Where(e => e.User.Id == session.User.Id && !e.WasRevoked)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);

            return TypedResults.Unauthorized();
        }

        if (session.ValidUntil < DateTime.UtcNow)
        {
            Logger.LogWarning("Expired refresh token used in refresh attempt for user {Email}", session.User.Email);
            session.WasRevoked = true;
            await db.SaveChangesAsync(ct);

            return TypedResults.Unauthorized();
        }

        await db.Sessions.Where(e => e.RefreshToken == req.RefreshToken)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);

        var refreshToken = UserUtils.GenerateRandomString(14);
        var deviceInfo = BrowserUtils.GetDeviceInfo(HttpContext.Request.Headers, uaDetector);

        await db.Sessions
            .Where(e => e.User.Id == session.User.Id && !e.WasRevoked && e.ValidUntil >= DateTime.UtcNow && e.DeviceInfo == deviceInfo)
            .ExecuteUpdateAsync(setters => setters.SetProperty(e => e.WasRevoked, true), ct);

        var newSession = new Session
        {
            Id = Ulid.NewUlid(),
            User = session.User,
            RefreshToken = refreshToken,
            DeviceInfo = deviceInfo,
            ValidUntil = DateTime.UtcNow.AddDays(7)
        };

        await db.Sessions.AddAsync(newSession, ct);
        await db.SaveChangesAsync(ct);

        Logger.LogInformation("Successfully refreshed token for user {Email} from {DeviceInfo}", session.User.Email, newSession.DeviceInfo);
        var response = new LoginResponse
        {
            AccessToken = UserUtils.CreateAccessToken(session.User, newSession.Id),
            RefreshToken = refreshToken,
            FirstName = session.User.FirstName,
            LastName = session.User.LastName
        };

        return TypedResults.Ok(response);
    }
}
