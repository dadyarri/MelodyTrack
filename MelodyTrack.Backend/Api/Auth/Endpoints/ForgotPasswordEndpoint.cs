using FastEndpoints;
using MelodyTrack.Backend.Utils;
using MelodyTrack.Common.Api.Auth.Requests;
using MelodyTrack.Common.Data;
using MelodyTrack.Common.Data.Models;
using MelodyTrack.Common.Utils;
using Microsoft.AspNetCore.Http.HttpResults;

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

        Logger.LogInformation(
            "User {Email} forgotten password and requested for its restoration. Here is his link for this: {Url}",
            req.Email, url);

        await db.PasswordRestorationRequests.AddAsync(restorationRequest, ct);
        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }
}