namespace MelodyTrack.ApiService.Endpoints.Auth.InitiateReset;

using Ardalis.Result;
using Common.Contracts.Auth.InitiateReset;
using FastEndpoints;
using Services;

public class InitiateResetEndpoint(AuthService authService)
    : Ep.Req<InitiateResetRequest>.Res<Result>
{
    public override void Configure()
    {
        Verbs(Http.POST);
        Routes("/auth/reset");
        AllowAnonymous();
    }

    public override async Task<Result> ExecuteAsync(InitiateResetRequest req, CancellationToken ct) =>
        await authService.InitiatePasswordResetAsync(req, ct);
}
