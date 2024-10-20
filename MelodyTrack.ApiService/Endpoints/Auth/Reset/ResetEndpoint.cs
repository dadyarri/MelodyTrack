namespace MelodyTrack.ApiService.Endpoints.Auth.Reset;

using FastEndpoints;
using Login;
using Services;

public class ResetEndpoint(AuthService authService) : Ep.Req<ResetRequest>.Res<LoginResponse>
{
    public override void Configure()
    {
        Verbs(Http.POST);
        Routes("/auth/reset/confirm");
        AllowAnonymous();
    }

    public override async Task<LoginResponse> ExecuteAsync(ResetRequest req, CancellationToken ct) =>
        await authService.ResetPasswordAsync(req, ct);
}
