namespace MelodyTrack.ApiService.Endpoints.Auth.Register;

using FastEndpoints;
using Login;
using Microsoft.AspNetCore.Http.HttpResults;

public class RegisterEndpoint : Ep
    .Req<RegisterRequest>
    .Res<Results<Ok<LoginResponse>, Conflict, ProblemDetails>>
{
    public override void Configure()
    {
        Verbs(Http.POST);
        Routes("/auth/register");
        AllowAnonymous();
    }

    public async override Task<Results<Ok<LoginResponse>, UnauthorizedHttpResult, ProblemDetails>> HandleAsync(
        RegisterRequest request, CancellationToken ct)
    {
        return TypedResults.Ok(new LoginResponse { AccessToken = "access-token", RefreshToken = "refresh-token" });
    }
}
