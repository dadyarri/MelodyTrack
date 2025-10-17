using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class RefreshEndpoint(AppDbContext db)
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
        var session = await db.Sessions
            .Where(e => e.RefreshToken == req.RefreshToken && !e.WasRevoked)
            .Include(e => e.User)
            .FirstOrDefaultAsync(ct);


        if (session is null)
        {
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
            DeviceInfo = BrowserUtils.GetDeviceInfo(HttpContext.Request.Headers.UserAgent),
            ValidUntil = DateTime.UtcNow.AddDays(7),
        };

        await db.Sessions.AddAsync(newSession, ct);
        await db.SaveChangesAsync(ct);

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