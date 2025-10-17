using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Serilog;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class ForgotPasswordEndpoint(AppDbContext db) : Ep.Req<ForgotPasswordRequest>.Res<NoContent>
{
    public override void Configure()
    {
        Post("/auth/forgotPassword");
        AllowAnonymous();
    }

    public override async Task<NoContent> ExecuteAsync(ForgotPasswordRequest req, CancellationToken ct)
    {
        var token = UserUtils.GenerateRandomString(14);
        var appDomain = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_APP_DOMAIN");
        var url = $"{appDomain}/restore?code={token}";
        var restorationRequest = new PasswordRestorationRequest
        {
            Id = Ulid.NewUlid(),
            Email = req.Email,
            Token = token,
            ValidUntil = DateTime.UtcNow.AddHours(2)
        };

        Log.Logger.Information(
            "User {Email} forgotten password and requested for its restoration. Here is his link for this: {Url}",
            req.Email, url);

        await db.PasswordRestorationRequests.AddAsync(restorationRequest, ct);
        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }
}