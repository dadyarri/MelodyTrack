namespace MelodyTrack.ApiService.Endpoints.Auth.Reset;

using System.Threading;
using System.Threading.Tasks;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;

public class ResetEndpoint : Ep.Req<RestoreRequest>.Res<Results<NoContent, ProblemDetails>>
{
    public override void Configure()
    {
        Verbs(Http.POST);
        Routes("/auth/reset");
        AllowAnonymous();
    }

    public override async Task<Results<NoContent, ProblemDetails>> ExecuteAsync(RestoreRequest req, CancellationToken ct)
    {
        // TODO: Add sending emails
        throw new NotImplementedException();
    }
}
