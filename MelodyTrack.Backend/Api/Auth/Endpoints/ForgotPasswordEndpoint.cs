using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class ForgotPasswordEndpoint(AppDbContext db)
    : Ep.Req<ForgotPasswordRequest>.Res<Ok<ForgotPasswordResponse>>
{
    public override void Configure()
    {
        Post("/auth/forgotPassword");
        AllowAnonymous();
    }

    public override async Task<Ok<ForgotPasswordResponse>> ExecuteAsync(ForgotPasswordRequest req, CancellationToken ct)
    {
        var email = req.Email.ToLowerInvariant();
        var token = UserUtils.GenerateRandomString(14);
        var appDomain = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_APP_DOMAIN");
        var url = $"{appDomain}/restore?code={token}";
        var restorationRequest = new PasswordRestorationRequest
        {
            Id = Ulid.NewUlid(),
            Email = email,
            Token = token,
            ValidUntil = DateTime.UtcNow.AddHours(2)
        };

        Logger.LogInformation(
            "User {Email} forgotten password and requested for its restoration. Here is his link for this: {Url}",
            email, url);

        await db.PasswordRestorationRequests.AddAsync(restorationRequest, ct);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new ForgotPasswordResponse
        {
            Token = token,
            Url = url
        });
    }
}
