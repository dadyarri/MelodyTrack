namespace MelodyTrack.ApiService.Endpoints.Auth.Me;

using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;

public class MeEndpoint : Ep.NoReq.Res<Results<Ok<MeResponse>, UnauthorizedHttpResult, ProblemDetails>>
{
    public override void Configure()
    {
        Verbs(Http.GET);
        Routes("/auth/me");
    }

    public async override Task<Results<Ok<MeResponse>, UnauthorizedHttpResult, ProblemDetails>> ExecuteAsync(
        CancellationToken ct)
    {
        return TypedResults.Ok(new MeResponse());
    }
}
