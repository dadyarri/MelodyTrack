namespace MelodyTrack.ApiService.Endpoints.Auth.Register;

using Ardalis.Result;
using FastEndpoints;
using Login;
using Services;

public class RegisterEndpoint(AuthService authService) : Ep.Req<RegisterRequest>.Res<Result<LoginResponse>>
{
    public override void Configure()
    {
        Verbs(Http.POST);
        Routes("/auth/register");
        AllowAnonymous();
    }

    public override async Task<Result<LoginResponse>> HandleAsync(RegisterRequest request, CancellationToken ct) =>
        await authService.RegisterUserAsync(request, ct);
}
