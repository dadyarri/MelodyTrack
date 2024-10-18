namespace MelodyTrack.ApiService.Endpoints.Auth.Refresh;

using FastEndpoints;
using Login;
using Microsoft.AspNetCore.Http.HttpResults;

public class RefreshEndpoint : Ep
    .Req<RefreshRequest>
    .Res<Results<Ok<LoginResponse>, UnauthorizedHttpResult, ProblemDetails>>
{
    public override void Configure()
    {
        Verbs(Http.POST);
        Routes("/auth/refresh");
    }

    public override async Task<Results<Ok<LoginResponse>, UnauthorizedHttpResult, ProblemDetails>> ExecuteAsync(
        RefreshRequest req, CancellationToken ct)
    {
        return TypedResults.Ok(new LoginResponse { AccessToken = "access-token", RefreshToken = "refresh-token" });
    }
}
