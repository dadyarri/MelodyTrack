using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class LoginEndpoint(AppDbContext db)
    : Ep.Req<LoginRequest>.Res<Results<Ok<LoginResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/login");
        AllowAnonymous();
    }

    public override async Task<Results<Ok<LoginResponse>, UnauthorizedHttpResult>> ExecuteAsync(LoginRequest req,
        CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(e => e.Email == req.Email, ct);

        if (user is null || UserUtils.IsValidPassword(user.Password, req.Password))
        {
            return TypedResults.Unauthorized();
        }

        await db.Sessions.Where(e => e.User == user)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);

        var refreshToken = UserUtils.GenerateRandomString(14);

        var session = new Session
        {
            Id = Ulid.NewUlid(),
            User = user,
            RefreshToken = refreshToken,
            DeviceInfo = req.DeviceInfo,
            ValidUntil = DateTime.UtcNow.AddDays(7),
        };

        await db.Sessions.AddAsync(session, ct);

        var response = new LoginResponse
        {
            AccessToken = UserUtils.CreateAccessToken(user),
            RefreshToken = refreshToken,
            FirstName = user.FirstName,
            LastName = user.LastName
        };

        return TypedResults.Ok(response);
    }
}