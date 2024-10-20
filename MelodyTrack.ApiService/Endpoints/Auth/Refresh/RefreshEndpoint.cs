namespace MelodyTrack.ApiService.Endpoints.Auth.Refresh;

using Ardalis.Result;
using Common.Contracts.Auth.Login;
using Common.Contracts.Auth.Refresh;
using FastEndpoints;
using Login;
using Services;

public class RefreshEndpoint(AuthService authService) : Ep.Req<RefreshRequest>.Res<Result<LoginResponse>>
{
    public override void Configure()
    {
        Verbs(Http.POST);
        Routes("/auth/refresh");
    }

    public override async Task<Result<LoginResponse>> ExecuteAsync(RefreshRequest req, CancellationToken ct) =>
        await authService.RefreshTokensAsync(req, ct);
}
