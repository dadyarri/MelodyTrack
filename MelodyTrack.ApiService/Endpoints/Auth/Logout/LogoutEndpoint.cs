namespace MelodyTrack.ApiService.Endpoints.Auth.Logout;

using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using ProblemDetails = FastEndpoints.ProblemDetails;

public class LogoutEndpoint : Ep.Req<LogoutRequest>.Res<Results<NoContent, ProblemDetails>>
{
    public override void Configure()
    {
        Verbs(Http.POST);
        Routes("/auth/logout");
    }

    public async override Task<Results<NoContent, ProblemDetails>> ExecuteAsync(LogoutRequest req, CancellationToken ct)
    {
        return TypedResults.NoContent();
    }
}
