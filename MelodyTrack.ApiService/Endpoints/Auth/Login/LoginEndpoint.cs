namespace MelodyTrack.ApiService.Endpoints.Auth.Login;

using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using ProblemDetails = FastEndpoints.ProblemDetails;

public class LoginEndpoint : Ep
    .Req<LoginRequest>
    .Res<Results<Ok<LoginResponse>, UnauthorizedHttpResult, ProblemDetails>>
{
    public override void Configure()
    {
        Verbs(Http.POST);
        Routes("/auth/login");
        AllowAnonymous();
    }

    public async override Task<Results<Ok<LoginResponse>, UnauthorizedHttpResult, ProblemDetails>> ExecuteAsync(
        LoginRequest request, CancellationToken ct)
    {
        if (request.Email != "me@example.com" || request.Password != "password")
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new LoginResponse { AccessToken = "access-token", RefreshToken = "refresh-token" });
    }
}
