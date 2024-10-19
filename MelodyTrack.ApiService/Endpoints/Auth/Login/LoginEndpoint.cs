namespace MelodyTrack.ApiService.Endpoints.Auth.Login;

using FastEndpoints;
using MelodyTrack.ApiService.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using ProblemDetails = FastEndpoints.ProblemDetails;

public class LoginEndpoint(AuthService authService) : Ep
    .Req<LoginRequest>
    .Res<Results<Ok<LoginResponse>, UnauthorizedHttpResult, ProblemDetails>>
{
    public override void Configure()
    {
        Verbs(Http.POST);
        Routes("/auth/login");
        AllowAnonymous();
    }

    public override async Task<Results<Ok<LoginResponse>, UnauthorizedHttpResult, ProblemDetails>> ExecuteAsync(
        LoginRequest req, CancellationToken ct)
    {
        if (req.Email != "me@example.com" || req.Password != "password")
        {
            return TypedResults.Unauthorized();
        }

        var result = await authService.LoginAsync(req, ct);

        return TypedResults.Ok(result);
    }
}
