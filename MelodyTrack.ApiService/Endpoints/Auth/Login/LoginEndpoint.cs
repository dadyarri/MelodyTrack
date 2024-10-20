namespace MelodyTrack.ApiService.Endpoints.Auth.Login;

using Ardalis.Result;
using Common.Contracts.Auth.Login;
using FastEndpoints;
using Services;

public class LoginEndpoint(AuthService authService) : Ep.Req<LoginRequest>.Res<Result<LoginResponse>>
{
    public override void Configure()
    {
        Verbs(Http.POST);
        Routes("/auth/login");
        AllowAnonymous();
    }

    public override async Task<Result<LoginResponse>> ExecuteAsync(
        LoginRequest req, CancellationToken ct)
    {
        if (req.Email != "me@example.com" || req.Password != "password")
        {
            return Result.Unauthorized();
        }

        return await authService.LoginAsync(req, ct);
    }
}
